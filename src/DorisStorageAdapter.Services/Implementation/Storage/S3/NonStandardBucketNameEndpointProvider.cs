using Amazon.Runtime.Endpoints;
using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using Amazon.S3.Internal;
using System;

namespace DorisStorageAdapter.Services.Implementation.Storage.S3;

internal sealed class NonStandardBucketNameEndpointProvider : IEndpointProvider
{
    private readonly AmazonS3EndpointProvider _inner = new();

    public Endpoint ResolveEndpoint(EndpointParameters parameters)
    {
        var endpoint = _inner.ResolveEndpoint(parameters);

        if (parameters["Bucket"] is not string bucket ||
            string.IsNullOrEmpty(bucket))
        {
            return endpoint;
        }

        // Same URI encoding used by AmazonS3EndpointProvider.
        var encodedBucket = Fn.UriEncode(bucket);

        // If AWS does not encode the bucket name, there is nothing to fix.
        //
        // This also means the bucket can still be a normal virtual-hostable
        // S3 bucket, so leave standard SDK behavior completely untouched.
        if (encodedBucket == bucket)
        {
            return endpoint;
        }

        // At this point the bucket contains characters that prevent normal
        // virtual-hosted-style addressing, so the ordinary S3 resolver falls
        // back to path-style addressing.
        //
        // Only apply the workaround if the resolved endpoint has exactly the
        // shape we expect: /<uri-encoded-bucket> as its final path component.
        var encodedBucketSuffix = "/" + encodedBucket;

        if (!endpoint.URL.EndsWith(
                encodedBucketSuffix,
                StringComparison.Ordinal))
        {
            return endpoint;
        }

        // AmazonS3EndpointProvider has already URI-encoded the bucket:
        //
        //   tenant:bucket
        //       ->
        //   tenant%3Abucket
        //
        // Later SigV4 canonicalization would encode the '%' again:
        //
        //   tenant%3Abucket
        //       ->
        //   tenant%253Abucket
        //
        // Restore the original bucket value here so canonicalization performs
        // the URI encoding exactly once.
        endpoint.URL =
            endpoint.URL[..^encodedBucket.Length]
            + bucket;

        return endpoint;
    }
}