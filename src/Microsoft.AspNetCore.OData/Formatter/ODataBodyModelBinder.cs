﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OData.Abstracts;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Formatter.Deserialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Microsoft.AspNetCore.OData.Formatter
{
    /// <summary>
    /// A model binder for ODataParameterValue values.
    /// </summary>
    /// <remarks>
    /// This class is similar to ODataModelBinderProvider in AspNet. The flow is similar but the
    /// type are dissimilar enough making a common version more complex than separate versions.
    /// </remarks>
    internal class ODataBodyModelBinder : IModelBinder
    {
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We don't want to fail in model binding.")]
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            if (bindingContext.ModelMetadata == null)
            {
                throw Error.Argument("bindingContext", SRResources.ModelBinderUtil_ModelMetadataCannotBeNull);
            }

            HttpContext httpContext = bindingContext.HttpContext;

            ODataFeature odataFeature = httpContext.ODataFeature() as ODataFeature;
            IDictionary<string, object> values = odataFeature?.BodyValues;
            if (values == null)
            {
                values = ReadODataBody(bindingContext);
                if (values == null)
                {
                    values = new Dictionary<string, object>();
                }

                if (odataFeature != null)
                {
                    odataFeature.BodyValues = values;
                }
            }

            if (values.TryGetValue(bindingContext.ModelMetadata.Name, out object result))
            {
                //ValueProviderResult valueProviderResult = new ValueProviderResult(result);
                //bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

                bindingContext.Result = ModelBindingResult.Success(result);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        internal static ODataDeserializerContext BuildDeserializerContext(ModelBindingContext bindingContext/*, IEdmTypeReference edmTypeReference*/)
        {
            HttpRequest request = bindingContext.HttpContext.Request;
            ODataPath path = request.ODataFeature().Path;
            IEdmModel edmModel = request.GetModel();

            return new ODataDeserializerContext
            {
                Path = path,
                Model = edmModel,
                Request = request,
                ResourceType = bindingContext.ModelType,
        //        ResourceEdmType = edmTypeReference,
            };
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
        public static IDictionary<string, object> ReadODataBody(ModelBindingContext bindingContext)
        {
            ODataActionPayloadDeserializer deserializer = bindingContext.HttpContext.Request.GetSubServiceProvider().GetService<ODataActionPayloadDeserializer>();
            if (deserializer == null)
            {
                return null;
            }

            ODataDeserializerContext context = BuildDeserializerContext(bindingContext);
            HttpRequest request = bindingContext.HttpContext.Request;

            var body = request.HttpContext.Features.Get<Http.Features.IHttpBodyControlFeature>();
            if (body != null)
            {
                body.AllowSynchronousIO = true;
            }

            IODataRequestMessage oDataRequestMessage =
                    ODataMessageWrapperHelper.Create(request.Body, request.Headers);
            IEdmModel model = request.GetModel();
            using (var messageReader = new ODataMessageReader(oDataRequestMessage, null, model))
            {
                var result = deserializer.Read(messageReader, typeof(ODataActionParameters), context);
                return result as ODataActionParameters;
            }
        }
    }
}
