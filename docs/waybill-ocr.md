# Đọc số điện thoại người nhận từ ảnh kiện hàng (OCR)

Shipper chụp MỘT ảnh kiện hàng (mặt có vận đơn). Ảnh vừa là ảnh bằng chứng gửi hàng (`ParcelImageUrl`)
vừa là đầu vào OCR. OCR chỉ đọc số điện thoại người nhận để Shipper đỡ gõ tay.

## Endpoint (header `X-Guest-Session-Token` bắt buộc)

| Endpoint | Việc |
|---|---|
| `POST /api/delivery-requests/{id}:waybill-ocr` (multipart, field `file`) | Lưu ảnh làm `ParcelImageUrl`, gọi OCR. Trả `{ status, phones[], isMasked, parcelImageUrl }`. Tối đa 3 lần mỗi phiên. |
| `POST /api/delivery-requests/{id}:resolve-recipient` `{ phone }` | Trả `{ fullName }` của cư dân đã đăng ký nhận hàng tại đúng tủ của phiên. Cần đã có ảnh kiện. Tối đa 5 lần mỗi phiên. |
| `POST /api/delivery-requests/{id}:submit` `{ parcelImageUrl?, recipientPhone }` | `parcelImageUrl` có thể bỏ trống để dùng ảnh đã lưu ở bước OCR. |

Mã lỗi mới: `delivery/ocr_limit_reached` (429), `delivery/lookup_limit_reached` (429),
`delivery/parcel_image_required` (409), `delivery/recipient_not_found` (404).

OCR thất bại không phải lỗi: `status = "Failed"`, Shipper nhập tay. Vận đơn che bớt số (`isMasked = true`)
không trả số nào để tránh điền sai.

## Cấu hình DeepSeek

Chỉ đặt qua secrets hoặc biến môi trường, KHÔNG commit API key.

```text
DeepSeek__ApiKey
DeepSeek__Enabled        (mặc định true)
DeepSeek__BaseUrl        (mặc định https://api.deepseek.com)
DeepSeek__Model          (mặc định deepseek-flash)
DeepSeek__TimeoutSeconds (mặc định 8)
```

Thiếu key hoặc `Enabled = false` thì OCR luôn trả `Failed`, luồng gửi hàng vẫn chạy bằng nhập tay.
Ảnh và phản hồi của DeepSeek không được ghi log; số điện thoại không được ghi vào audit log.
