﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.OData.E2E.Tests.Query.ActionResult
{
    public class Customer
    {
        public string Id { get; set; }

        public IEnumerable<Book> Books { get; set; }
    }

    public class Book
    {
        public string Id { get; set; }
    }
}
