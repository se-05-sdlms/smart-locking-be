using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.DeliveryRequests;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Infrastructure.Options;

namespace smart_locking_be.Infrastructure.Services;

/// <summary>
/// Đọc số điện thoại người nhận từ ảnh kiện hàng bằng model vision của DeepSeek.
/// Không log ảnh hay nội dung phản hồi vì có dữ liệu cá nhân; mọi lỗi đều trả Failed để Shipper nhập tay.
/// </summary>
public sealed class DeepSeekWaybillOcrService(
    HttpClient httpClient,
    IOptions<DeepSeekOptions> options,
    ILogger<DeepSeekWaybillOcrService> logger) : IWaybillOcrService
{
    private const string SystemPrompt =
        "Bạn trích xuất số điện thoại NGƯỜI NHẬN từ ảnh kiện hàng có dán vận đơn tại Việt Nam. " +
        "Chỉ lấy số điện thoại nằm trong khối người nhận (Người nhận / Đến / To / Receiver / Consignee). " +
        "TUYỆT ĐỐI không lấy số của người gửi (Người gửi / Từ / From / Sender). " +
        "Không đọc tên, địa chỉ hay trường nào khác. " +
        "Nếu số bị che bớt bằng ký tự * hoặc x thì đặt isMasked=true và không đoán số còn thiếu. " +
        "Chỉ trả về JSON hợp lệ dạng {\"phones\":[\"0901234567\"],\"isMasked\":false}. " +
        "Không có số người nhận thì phones là mảng rỗng.";

    private const int MaxPhones = 3;

    public async Task<WaybillOcrResult> ExtractRecipientPhonesAsync(
        byte[] image,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        DeepSeekOptions settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey) || image.Length == 0)
        {
            return WaybillOcrResult.Failed;
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 30)));

        try
        {
            var body = new
            {
                model = settings.Model,
                temperature = 0,
                max_tokens = 200,
                thinking = new { type = "disabled" },
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Trích xuất số điện thoại người nhận (JSON)." },
                            new
                            {
                                type = "image_url",
                                image_url = new { url = $"data:{contentType};base64,{Convert.ToBase64String(image)}" }
                            }
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using HttpResponseMessage response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Waybill OCR provider returned HTTP {StatusCode}.", (int)response.StatusCode);
                return WaybillOcrResult.Failed;
            }

            ChatCompletion? completion = await response.Content.ReadFromJsonAsync<ChatCompletion>(timeout.Token);
            string? content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            return Parse(content);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException)
        {
            // Never log provider payloads or the image; only the failure category.
            logger.LogWarning("Waybill OCR failed: {Reason}.", exception.GetType().Name);
            return WaybillOcrResult.Failed;
        }
    }

    internal static WaybillOcrResult Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return WaybillOcrResult.Failed;
        }

        OcrPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OcrPayload>(content);
        }
        catch (JsonException)
        {
            return WaybillOcrResult.Failed;
        }

        if (payload is null)
        {
            return WaybillOcrResult.Failed;
        }

        // A masked number is never trusted, even if the model returned digits for it.
        if (payload.IsMasked)
        {
            return new WaybillOcrResult(false, [], true);
        }

        List<string> phones = (payload.Phones ?? [])
            .Select(VietnamPhoneNumber.Normalize)
            .OfType<string>()
            .Distinct()
            .Take(MaxPhones)
            .ToList();

        return phones.Count > 0 ? new WaybillOcrResult(true, phones, false) : WaybillOcrResult.Failed;
    }

    private sealed record ChatCompletion(
        [property: JsonPropertyName("choices")] List<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] Message? Message);

    private sealed record Message(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OcrPayload(
        [property: JsonPropertyName("phones")] List<string>? Phones,
        [property: JsonPropertyName("isMasked")] bool IsMasked);
}
