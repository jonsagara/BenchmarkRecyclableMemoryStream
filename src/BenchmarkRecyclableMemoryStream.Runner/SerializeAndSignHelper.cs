using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BenchmarkRecyclableMemoryStream.Runner
{
    public static class SerializeAndSignHelper
    {
        private static readonly byte[] _hmacKey = Encoding.ASCII.GetBytes("this is the super secure key oh my aren't you impressed");

        /// <summary>
        /// Serialize to strings using camelCase property names.
        /// </summary>
        private static readonly JsonSerializerSettings _camelCaseSettings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        /// <summary>
        /// Serialize to Streams using camelCase property names.
        /// </summary>
        private static readonly JsonSerializer _camelCaseSerializer = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        /// <summary>
        /// <para>I know I'll be sending my payload over the network, so don't include the UTF-8 BOM when serializing
        /// to a stream.</para>
        /// <para>See: https://tools.ietf.org/html/rfc8259#section-8.1 </para>
        /// </summary>
        private static readonly UTF8Encoding _utf8EncodingNoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Obtain RecyclableMemoryStreams from this singleton.
        /// </summary>
        private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new RecyclableMemoryStreamManager();


        /// <summary>
        /// Serialize the object to a JSON string, then compute a hash of the JSON byte[].
        /// </summary>
        public static string SerializeToStringAndSign(object value)
        {
            // Serialize to a JSON string.
            var widgetEnvJson = JsonConvert.SerializeObject(value, _camelCaseSettings);

            // Get the byte[] representation of the JSON string.
            var itemsJsonBytes = Encoding.UTF8.GetBytes(widgetEnvJson);

            // Hash the JSON byte[].
            using var hmac = new HMACSHA256(key: _hmacKey);
            var hashedBytes = hmac.ComputeHash(itemsJsonBytes);

            return ToFriendlyHashString(hashedBytes);
        }

        /// <summary>
        /// Serialize to a new MemoryStream, then compute the hash of the stream contents.
        /// </summary>
        public static async Task<string> SerializeToMemoryStreamAndSign(object value)
        {
            // *** 
            // Every invocation creates a new MemoryStream. If value is large, the jsonStream
            //   will end up on the LOH.
            // ***
            using (var jsonStream = new MemoryStream())
            {
                // Serialize the object JSON into the stream. Leave the stream open so that we can compute
                //   its hash.
                using (var streamWriter = new StreamWriter(jsonStream, _utf8EncodingNoBOM, leaveOpen: true))
                {
                    _camelCaseSerializer.Serialize(streamWriter, value);
                }

                // Hash the JSON stream.
                jsonStream.Position = 0L;

                using var hmac = new HMACSHA256(key: _hmacKey);
                var hashedBytes = await hmac.ComputeHashAsync(jsonStream);

                return ToFriendlyHashString(hashedBytes);
            }
        }

        /// <summary>
        /// Serialize to a RecyclableMemoryStream, then compute the hash of the stream contents.
        /// </summary>
        public static async Task<string> SerializeToRecyclableMemoryStreamAndSign(object value)
        {
            // ***
            // Every invocation gets a RecyclableMemoryStream from the recyclable memory stream manager. It uses
            //   preallocated, pooled buffers to avoid runaway allocations on the LOH.
            // ***
            using (var jsonStream = _memoryStreamManager.GetStream(tag: nameof(SerializeToRecyclableMemoryStreamAndSign)))
            {
                // Serialize the object JSON into the stream. Leave the stream open so that we can compute
                //   its hash.
                using (var streamWriter = new StreamWriter(jsonStream, _utf8EncodingNoBOM, leaveOpen: true))
                {
                    _camelCaseSerializer.Serialize(streamWriter, value);
                }

                // Hash the JSON stream.
                jsonStream.Position = 0L;

                using var hmac = new HMACSHA256(key: _hmacKey);
                var hashedBytes = await hmac.ComputeHashAsync(jsonStream);

                return ToFriendlyHashString(hashedBytes);
            }
        }


        //
        // Private methods
        //

        private static string ToFriendlyHashString(byte[] hashedBytes)
            => BitConverter.ToString(hashedBytes).Replace("-", string.Empty).ToLowerInvariant();
    }
}
