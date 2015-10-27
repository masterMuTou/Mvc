// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNet.Mvc.Routing;
using Microsoft.AspNet.Routing;

namespace Microsoft.AspNet.Mvc.Internal.Routing
{
    public struct LinkGenerationMatch
    {
        private readonly bool _isFallbackMatch;
        private readonly AttributeRouteLinkGenerationEntry _entry;

        public LinkGenerationMatch(
            AttributeRouteLinkGenerationEntry entry,
            bool isFallbackMatch,
            VirtualPathContext pathContext)
        {
            _entry = entry;
            _isFallbackMatch = isFallbackMatch;
            RouteValues = pathContext.Values;
            AmbientValues = pathContext.AmbientValues;
        }

        public AttributeRouteLinkGenerationEntry Entry { get { return _entry; } }

        public bool IsFallbackMatch { get { return _isFallbackMatch; } }

        public IDictionary<string, object> RouteValues { get; }

        public IDictionary<string, object> AmbientValues { get; }

        public int ParametersWithValues
        {
            get
            {
                var t = this;
                return Entry?.Template?.Parameters?.Count(p => (t.RouteValues.ContainsKey(p.Name) && t.RouteValues[p.Name] != p.DefaultValue) ||
                    (t.AmbientValues.ContainsKey(p.Name) && t.AmbientValues[p.Name] != p.DefaultValue)) ?? 0;
            }
        }

        public int DefaultParameters
        {
            get
            {
                var t = this;
                return Entry?.Defaults?.Count(p => t.RouteValues.ContainsKey(p.Key)) ?? 0;
            }
        }
    }
}