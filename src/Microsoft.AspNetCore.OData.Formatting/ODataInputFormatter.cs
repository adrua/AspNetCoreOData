﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.OData.Abstracts;
using Microsoft.AspNetCore.OData.Formatting.Deserialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Microsoft.AspNetCore.OData.Formatting
{
    /// <summary>
    /// <see cref="TextInputFormatter"/> class to handle OData.
    /// </summary>
    public class ODataInputFormatter : TextInputFormatter
    {
        /// <summary>
        /// The set of payload kinds this formatter will accept in CanReadType.
        /// </summary>
        private readonly IEnumerable<ODataPayloadKind> _payloadKinds;

        /// <summary>
        /// Initializes a new instance of the <see cref="ODataInputFormatter"/> class.
        /// </summary>
        /// <param name="payloadKinds">The kind of payloads this formatter supports.</param>
        public ODataInputFormatter(IEnumerable<ODataPayloadKind> payloadKinds)
        {
            if (payloadKinds == null)
            {
                throw Error.ArgumentNull("payloadKinds");
            }

            _payloadKinds = payloadKinds;
        }

        /// <summary>
        /// Gets or sets a method that allows consumers to provide an alternate base
        /// address for OData Uri.
        /// </summary>
        public Func<HttpRequest, Uri> BaseAddressFactory { get; set; }

        /// <inheritdoc/>
        public override bool CanRead(InputFormatterContext context)
        {
            if (context == null)
            {
                throw Error.ArgumentNull("context");
            }

            HttpRequest request = context.HttpContext.Request;
            if (request == null)
            {
                throw Error.InvalidOperation(SRResources.ReadFromStreamAsyncMustHaveRequest);
            }

            // Ignore non-OData requests.
            if (request.ODataFeature().Path == null)
            {
                return false;
            }

            Type type = context.ModelType;
            if (type == null)
            {
                throw Error.ArgumentNull("type");
            }

            IEdmTypeReference expectedPayloadType;
            ODataDeserializer deserializer = GetDeserializer(type, request, out expectedPayloadType);
            if (deserializer != null)
            {
                return _payloadKinds.Contains(deserializer.ODataPayloadKind);
            }

            return false;
        }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "The caught exception type is reflected into a faulted task.")]
        public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
        {
            if (context == null)
            {
                throw Error.ArgumentNull("context");
            }

            Type type = context.ModelType;
            if (type == null)
            {
                throw Error.ArgumentNull("type");
            }

            HttpRequest request = context.HttpContext.Request;
            if (request == null)
            {
                throw Error.InvalidOperation(SRResources.ReadFromStreamAsyncMustHaveRequest);
            }

            // If content length is 0 then return default value for this type
            RequestHeaders contentHeaders = request.GetTypedHeaders();
            object defaultValue = GetDefaultValueForType(type);
            if (contentHeaders == null || contentHeaders.ContentLength == 0)
            {
                return Task.FromResult(InputFormatterResult.Success(defaultValue));
            }

            try
            {
                var body = request.HttpContext.Features.Get<AspNetCore.Http.Features.IHttpBodyControlFeature>();
                if (body != null)
                {
                    body.AllowSynchronousIO = true;
                }

                Func<ODataDeserializerContext> getODataDeserializerContext = () =>
                {
                    return new ODataDeserializerContext
                    {
                        Request = request,
                    };
                };

                Action<Exception> logErrorAction = (ex) =>
                {
                    ILogger logger = context.HttpContext.RequestServices.GetService<ILogger>();
                    if (logger == null)
                    {
                        throw ex;
                    }

                    logger.LogError(ex, String.Empty);
                };

                List<IDisposable> toDispose = new List<IDisposable>();

                object result = ReadFromStream(
                    type,
                    defaultValue,
                    GetBaseAddressInternal(request),
                    request,
                    (disposable) => toDispose.Add(disposable),
                    logErrorAction);

                foreach (IDisposable obj in toDispose)
                {
                    obj.Dispose();
                }

                return Task.FromResult(InputFormatterResult.Success(result));
            }
            catch (Exception ex)
            {
                context.ModelState.AddModelError(context.ModelName, ex, context.Metadata);
                return Task.FromResult(InputFormatterResult.Failure());
            }
        }

        private static ODataDeserializerContext GetODataDeserializerContext(HttpRequest request)
        {
            return new ODataDeserializerContext
            {
                Request = request,
            };
        }

       internal static object ReadFromStream(
            Type type,
            object defaultValue,
            Uri baseAddress,
            HttpRequest request,
            Action<IDisposable> registerForDisposeAction,
            Action<Exception> logErrorAction)
        {
            object result;
            IEdmModel model = request.GetModel();
            IEdmTypeReference expectedPayloadType;
            ODataDeserializer deserializer = GetDeserializer(type, request, out expectedPayloadType);
            if (deserializer == null)
            {
                throw Error.Argument("type", SRResources.FormatterReadIsNotSupportedForType, type.FullName, typeof(ODataInputFormatter).FullName);
            }

            try
            {
                ODataMessageReaderSettings oDataReaderSettings = request.GetReaderSettings();
                oDataReaderSettings.BaseUri = baseAddress;
                oDataReaderSettings.Validations = oDataReaderSettings.Validations & ~ValidationKinds.ThrowOnUndeclaredPropertyForNonOpenType;

                IODataRequestMessage oDataRequestMessage =
                    ODataMessageWrapperHelper.Create(request.Body, request.Headers/*, request.GetODataContentIdMapping(), request.GetRequestContainer()*/);
                ODataMessageReader oDataMessageReader = new ODataMessageReader(oDataRequestMessage, oDataReaderSettings, model);
                registerForDisposeAction(oDataMessageReader);

                ODataPath path = request.ODataFeature().Path;
                ODataDeserializerContext readContext = GetODataDeserializerContext(request);
                readContext.Path = path;
                readContext.Model = model;
                readContext.ResourceType = type;
                readContext.ResourceEdmType = expectedPayloadType;

                result = deserializer.Read(oDataMessageReader, type, readContext);
            }
            catch (Exception e)
            {
                logErrorAction(e);
                result = defaultValue;
            }

            return result;
        }

        /// <summary>
        /// Internal method used for selecting the base address to be used with OData uris.
        /// If the consumer has provided a delegate for overriding our default implementation,
        /// we call that, otherwise we default to existing behavior below.
        /// </summary>
        /// <param name="request">The HttpRequest object for the given request.</param>
        /// <returns>The base address to be used as part of the service root; must terminate with a trailing '/'.</returns>
        private Uri GetBaseAddressInternal(HttpRequest request)
        {
            if (BaseAddressFactory != null)
            {
                return BaseAddressFactory(request);
            }
            else
            {
                return ODataInputFormatter.GetDefaultBaseAddress(request);
            }
        }

        /// <summary>
        /// Returns a base address to be used in the service root when reading or writing OData uris.
        /// </summary>
        /// <param name="request">The HttpRequest object for the given request.</param>
        /// <returns>The base address to be used as part of the service root in the OData uri; must terminate with a trailing '/'.</returns>
        public static Uri GetDefaultBaseAddress(HttpRequest request)
        {
            if (request == null)
            {
                throw Error.ArgumentNull("request");
            }

            LinkGenerator linkGenerator = request.HttpContext.RequestServices.GetRequiredService<LinkGenerator>();
            if (linkGenerator != null)
            {
                //   string uri = linkGenerator.GetUriByAction(request.HttpContext);

                Endpoint endPoint = request.HttpContext.GetEndpoint();
                EndpointNameMetadata name = endPoint.Metadata.GetMetadata<EndpointNameMetadata>();

                string aUri = linkGenerator.GetUriByName(request.HttpContext, name.EndpointName,
                    request.RouteValues, request.Scheme, request.Host, request.PathBase);

                return new Uri(aUri);
            }

            //string baseAddress = request.GetUrlHelper().CreateODataLink();
            //if (baseAddress == null)
            //{
            //    throw new SerializationException(SRResources.UnableToDetermineBaseUrl);
            //}

            //return baseAddress[baseAddress.Length - 1] != '/' ? new Uri(baseAddress + '/') : new Uri(baseAddress);

            return null;
        }

        internal static ODataVersion GetODataResponseVersion(HttpRequest request)
        {
            // OData protocol requires that you send the minimum version that the client needs to know to
            // understand the response. There is no easy way we can figure out the minimum version that the client
            // needs to understand our response. We send response headers much ahead generating the response. So if
            // the requestMessage has a OData-MaxVersion, tell the client that our response is of the same
            // version; else use the DataServiceVersionHeader. Our response might require a higher version of the
            // client and it might fail. If the client doesn't send these headers respond with the default version
            // (V4).
            return request.ODataMaxServiceVersion() ??
                request.ODataServiceVersion() ??
                ODataVersionConstraint.DefaultODataVersion;
        }

        private static ODataDeserializer GetDeserializer(
           Type type, HttpRequest request,  out IEdmTypeReference expectedPayloadType)
        {
            ODataPath path = request.ODataFeature().Path;
            IEdmModel model = request.GetModel();

            ODataDeserializerProvider deserializerProvider
                = request.HttpContext.RequestServices.GetRequiredService<ODataDeserializerProvider>();

            expectedPayloadType = EdmLibHelper.GetExpectedPayloadType(type, path, model);

            // Get the deserializer using the CLR type first from the deserializer provider.
            ODataDeserializer deserializer = deserializerProvider.GetODataDeserializer(type, request);
            if (deserializer == null && expectedPayloadType != null)
            {
                // we are in typeless mode, get the deserializer using the edm type from the path.
                deserializer = deserializerProvider.GetEdmTypeDeserializer(expectedPayloadType);
            }

            return deserializer;
        }
    }
}