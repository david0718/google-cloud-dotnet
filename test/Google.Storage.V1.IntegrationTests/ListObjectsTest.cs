﻿// Copyright 2015 Google Inc. All Rights Reserved.
// Licensed under the Apache License Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Object = Google.Apis.Storage.v1.Data.Object;

namespace Google.Storage.V1.IntegrationTests
{
    public class ListObjectsTest
    {
        private static readonly string[] s_allObjectNames =
            { "foo.txt", "bar.txt", "a/o1.txt", "a/o2.txt", "a/x/o3.txt", "a/x/o4.txt", "b/o5.txt", "multiversion.txt", "updated.txt" };

        private static readonly CloudConfiguration s_config = CloudConfiguration.Instance;
        private static readonly string s_bucket = s_config.TempBucketPrefix + "0";

        [Theory]
        [InlineData(null)]
        [InlineData(4)]
        public async Task AllObjects(int? pageSize)
        {
            var options = new ListObjectsOptions { PageSize = pageSize };
            await AssertObjects(null, options, s_allObjectNames);
        }

        [Theory]
        [InlineData(null, "foo.txt,bar.txt,multiversion.txt,updated.txt")]
        [InlineData("fo", "foo.txt")]
        [InlineData("a/", "a/o1.txt,a/o2.txt")]
        [InlineData("a/x/", "a/x/o3.txt,a/x/o4.txt")]
        [InlineData("missing/", "")]
        public async Task PrefixAndDelimiter(string prefix, string expectedNames)
        {
            var options = new ListObjectsOptions { Delimiter = "/" };
            await AssertObjects(prefix, options, expectedNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public async Task CancellationTokenRespected()
        {
            var cts = new CancellationTokenSource();
            var task = s_config.Client.ListAllObjectsAsync(s_bucket, null, cancellationToken: cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);            
        }

        [Fact]
        public async Task MultipleVersions()
        {
            var name = "multiversion.txt";

            // List the versions separately
            await AssertObjects(name, new ListObjectsOptions { Versions = true }, name, name);
            // Explicitly don't list the versions separately
            await AssertObjects(name, new ListObjectsOptions { Versions = false }, name);
            // Implicitly don't list the versions separately (the API default)
            await AssertObjects(name, null, name);
        }

        private async Task AssertObjects(string prefix, ListObjectsOptions options, params string[] expectedNames)
        {
            var actual = s_config.Client.ListObjects(s_bucket, prefix, options);
            AssertObjectNames(actual, expectedNames);
            actual = await s_config.Client.ListAllObjectsAsync(s_bucket, prefix, options, CancellationToken.None);
            AssertObjectNames(actual, expectedNames);
        }

        private void AssertObjectNames(IEnumerable<Object> actualObjects, string[] expectedNames)
        {
            var actualNames = actualObjects.Select(x => x.Name).OrderBy(x => x).ToList();
            Assert.Equal(expectedNames.OrderBy(x => x), actualNames);
        }
    }
}
