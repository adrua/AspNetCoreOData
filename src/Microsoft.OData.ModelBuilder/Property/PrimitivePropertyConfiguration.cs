﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.OData.Edm;

namespace Microsoft.OData.ModelBuilder
{
    /// <summary>
    /// Used to configure a primitive property of an entity type or complex type.
    /// This configuration functionality is exposed by the model builder Fluent API, see <see cref="ODataModelBuilder"/>.
    /// </summary>
    public class PrimitivePropertyConfiguration : StructuralPropertyConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrimitivePropertyConfiguration"/> class.
        /// </summary>
        /// <param name="property">The name of the property.</param>
        /// <param name="declaringType">The declaring EDM type of the property.</param>
        public PrimitivePropertyConfiguration(PropertyInfo property, StructuralTypeConfiguration declaringType)
            : base(property, declaringType)
        {
        }

        /// <summary>
        /// Gets or sets a value string representation of default value.
        /// </summary>
        public string DefaultValueString { get; set; }

        /// <summary>
        /// Gets the type of this property.
        /// </summary>
        public override PropertyKind Kind => PropertyKind.Primitive;

        /// <summary>
        /// Gets the backing CLR type of this property type.
        /// </summary>
        public override Type RelatedClrType => PropertyInfo.PropertyType;

        /// <summary>
        /// Gets the target Edm type kind of this property. Call the extension methods to set this property.
        /// </summary>
        public EdmPrimitiveTypeKind? TargetEdmTypeKind { get; internal set; }

        /// <summary>
        /// Configures the property to be optional.
        /// </summary>
        /// <returns>Returns itself so that multiple calls can be chained.</returns>
        public PrimitivePropertyConfiguration IsNullable()
        {
            NullableProperty = true;
            return this;
        }

        /// <summary>
        /// Configures the property to be required.
        /// </summary>
        /// <returns>Returns itself so that multiple calls can be chained.</returns>
        public PrimitivePropertyConfiguration IsRequired()
        {
            NullableProperty = false;
            return this;
        }

        /// <summary>
        /// Configures the property to be used in concurrency checks. For OData this means to be part of the ETag.
        /// </summary>
        /// <returns>Returns itself so that multiple calls can be chained.</returns>
        public PrimitivePropertyConfiguration IsConcurrencyToken()
        {
            ConcurrencyToken = true;
            return this;
        }
    }
}