using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenPatro.Models;

namespace OpenPatro.Services;

/// <summary>
/// HTTP client for the NepaliPatro API endpoints (Rashifal, Shubha Sait, Date Convert).
/// Includes AES-128-CBC "Scheme B" decryption for the Shubha Sait endpoint.
/// </summary>
public sealed class NepaliPatroApiClient
{
    private static readonly Uri RashifalUri = new("https://nepalipatro.com.np/rashifal/getv5/type/dwmy", UriKind.Absolute);
    private static readonly Uri ShubhaSaitUri = new("https://api.nepalipatro.com.np/sait/getv5", UriKind.Absolute);
    private static readonly Uri DateConvertUri = new("https://api.nepalipatro.com.np/calendars/dateConvert", UriKind.Absolute);

    // Scheme B AES-128-CBC parameters (raw UTF-8 bytes)
    private static readonly byte[] SchemeBKey = Encoding.UTF8.GetBytes("a2jqb2nw5266etzl");
    private static readonly byte[] SchemeBIv = Encoding.UTF8.GetBytes("lnous4x06o82jux5");

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public NepaliPatroApiClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0");
    }

    /// <summary>
    /// Fetch rashifal data. Returns plain JSON, no decryption needed.
    /// </summary>
    public async Task<RashifalResponse?> FetchRashifalAsync(CancellationToken cancellationToken = default)
    {
        var json = await _httpClient.GetStringAsync(RashifalUri, cancellationToken);
        return JsonSerializer.Deserialize<RashifalResponse>(json);
    }

    /// <summary>
    /// Fetch raw Shubha Sait data, decrypt with Scheme B, return as raw JSON string.
    /// The caller is responsible for parsing this into the nested dictionary structure.
    /// </summary>
    public async Task<string> FetchShubhaSaitRawJsonAsync(CancellationToken cancellationToken = default)
    {
        var hexResponse = await _httpClient.GetStringAsync(ShubhaSaitUri, cancellationToken);
        return DecryptSchemeB(hexResponse);
    }

    /// <summary>
    /// Convert a date between BS and AD.
    /// </summary>
    public async Task<DateConvertResponse?> ConvertDateAsync(DateConvertRequest request, CancellationToken cancellationToken = default)
    {
        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(DateConvertUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<DateConvertResponse>(responseJson);
    }

    /// <summary>
    /// Scheme B decryption:
    /// 1. Reverse the entire hex string character by character
    /// 2. Hex-decode the reversed string into a byte array
    /// 3. Decrypt using AES-128-CBC with the hardcoded key/IV
    /// 4. Strip PKCS7 padding
    /// 5. UTF-8 decode → base64 string
    /// 6. Base64-decode → UTF-8 decode → JSON string
    /// </summary>
    internal static string DecryptSchemeB(string hexResponse)
    {
        // Step 1: Reverse the hex string
        var reversed = new string(hexResponse.Reverse().ToArray());

        // Step 2: Hex-decode into byte array
        var ciphertext = HexStringToBytes(reversed);

        // Step 3: AES-128-CBC decrypt
        byte[] decryptedBytes;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None; // We strip PKCS7 manually
            aes.Key = SchemeBKey;
            aes.IV = SchemeBIv;

            using var decryptor = aes.CreateDecryptor();
            decryptedBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        // Step 4: Strip PKCS7 padding manually
        if (decryptedBytes.Length > 0)
        {
            var paddingValue = decryptedBytes[decryptedBytes.Length - 1];
            if (paddingValue is >= 1 and <= 16 && paddingValue <= decryptedBytes.Length)
            {
                // Verify all padding bytes match
                var valid = true;
                for (var i = decryptedBytes.Length - paddingValue; i < decryptedBytes.Length; i++)
                {
                    if (decryptedBytes[i] != paddingValue)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    decryptedBytes = decryptedBytes[..(decryptedBytes.Length - paddingValue)];
                }
            }
        }

        // Step 5: UTF-8 decode → base64 string
        var base64String = Encoding.UTF8.GetString(decryptedBytes);

        // Step 6: Base64-decode → UTF-8 decode → JSON string
        var jsonBytes = Convert.FromBase64String(base64String);
        return Encoding.UTF8.GetString(jsonBytes);
    }

    private static byte[] HexStringToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
