﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Xunit;

namespace Microsoft.AspNetCore.Mvc.Internal
{
    public class PrefixContainerTest
    {
        [Fact]
        public void ContainsPrefix_EmptyCollection_EmptyString_False()
        {
            // Arrange
            var keys = new string[] { };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(string.Empty);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ContainsPrefix_HasEntries_EmptyString_True()
        {
            // Arrange
            var keys = new string[] { "some.prefix" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(string.Empty);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("abc")]
        [InlineData("bc")]
        [InlineData("d")]
        public void ContainsPrefix_ReturnsTrue_IfTheContainerHasAnExactMatch(string prefix)
        {
            // Arrange
            var keys = new string[] { "bc", "a", "abc", "d" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(prefix);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("b")]
        [InlineData("c")]
        [InlineData("d")]
        public void ContainsPrefix_HasEntries_NoMatch(string prefix)
        {
            // Arrange
            var keys = new string[] { "ax", "bx", "cx", "dx" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(prefix);

            // Assert
            Assert.False(result);
        }


        [Theory]
        [InlineData("a")]
        [InlineData("b")]
        [InlineData("b.xy")]
        [InlineData("c")]
        [InlineData("c.x")]
        [InlineData("c.x.y")]
        [InlineData("d")]
        [InlineData("d.x")]
        [InlineData("d.x.z")]
        public void ContainsPrefix_HasEntries_PrefixMatch_WithDot(string prefix)
        {
            // Arrange
            var keys = new string[] { "a.x", "b.xy", "c.x.y", "d.x.z[0]" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(prefix);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("ab")]
        [InlineData("a.b.c")]
        [InlineData("a.b[1]")]
        [InlineData("a.b0")]
        public void ContainsPrefix_ReturnsFalse_IfPrefixDoesNotMatch(string prefix)
        {
            // Arrange
            var keys = new string[] { "a.b", "a.bc", "a.b[c]", "a.b[0]" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(prefix);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("a[x]")]
        [InlineData("d[x]")]
        [InlineData("d[x].y")]
        [InlineData("e")]
        [InlineData("e.a.b")]
        [InlineData("e.a.b[foo]")]
        [InlineData("e.a.b[foo].bar")]
        public void ContainsPrefix_HasEntries_PrefixMatch_WithSquareBrace(string prefix)
        {
            // Arrange
            var keys = new string[] { "a[x]", "d[x].y", "e.a.b[foo].bar" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.ContainsPrefix(prefix);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("foo")]
        public void GetKeysFromPrefix_ReturnsEmptySequenceWhenContainerIsEmpty(string prefix)
        {
            // Arrange
            var keys = new string[0];
            var container = new PrefixContainer(keys);

            // Act
            var result = container.GetKeysFromPrefix(prefix);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetKeysFromPrefix_ReturnsUniqueTopLevelEntries_WhenPrefixIsEmpty()
        {
            // Arrange
            var keys = new[] { "[0].name", "[0].address.street", "[item1].name", "[item1].age", "foo", "foo.bar" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.GetKeysFromPrefix(prefix: string.Empty);

            // Assert
            Assert.Collection(result.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase),
                item =>
                {
                    Assert.Equal("0", item.Key);
                    Assert.Equal("[0]", item.Value);
                },
                item =>
                {
                    Assert.Equal("foo", item.Key);
                    Assert.Equal("foo", item.Value);
                },
                item =>
                {
                    Assert.Equal("item1", item.Key);
                    Assert.Equal("[item1]", item.Value);
                });
        }

        [Fact]
        public void GetKeysFromPrefix_ReturnsEmptyDictionaryWhenNoKeysStartWithPrefix()
        {
            // Arrange
            var keys = new[] { "foo[0].name", "foo.age", "[1].name", "[item].age" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.GetKeysFromPrefix("baz");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetKeysFromPrefix_ReturnsSubKeysThatStartWithPrefix()
        {
            // Arrange
            var keys = new[] { "foo[0].name", "foo.age", "foo[1].name", "food[item].spice" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.GetKeysFromPrefix("foo");

            // Assert
            Assert.Collection(result.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase),
                item =>
                {
                    Assert.Equal("0", item.Key);
                    Assert.Equal("foo[0]", item.Value);
                },
                item =>
                {
                    Assert.Equal("1", item.Key);
                    Assert.Equal("foo[1]", item.Value);
                },
                item =>
                {
                    Assert.Equal("age", item.Key);
                    Assert.Equal("foo.age", item.Value);
                });
        }

        [Fact]
        public void GetKeysFromPrefix_ReturnsSubKeysThatStartWithPrefix_ForNestedSubKeys()
        {
            // Arrange
            var keys = new[] { "person[0].address[0].street", "person[0].address[1].street", "person[0].address[1].zip" };
            var container = new PrefixContainer(keys);

            // Act
            var result = container.GetKeysFromPrefix("person[0].address");

            // Assert
            Assert.Collection(result.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase),
                item =>
                {
                    Assert.Equal("0", item.Key);
                    Assert.Equal("person[0].address[0]", item.Value);
                },
                item =>
                {
                    Assert.Equal("1", item.Key);
                    Assert.Equal("person[0].address[1]", item.Value);
                });
        }
    }
}
