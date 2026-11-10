# Mã lấy hàng trả (return pickup code)

Quyết định đã chốt (issue #158): mã lấy hàng trả gồm **đúng 6 chữ số** (`^[0-9]{6}$`), không dùng chữ cái.
"6 ký tự chữ-số" trong SRS (BR-37..39, mục 3.10.3) được hiểu là 6 chữ số.

| Nơi | Hành vi |
|---|---|
| Backend (`ReturnRequestService`) | Sinh ngẫu nhiên 6 chữ số, không trùng với mã đang dùng; chỉ nhận đúng 6 chữ số khi validate; sai định dạng trả 400 `pickupCode`. |
| Shipper | Ô nhập OTP 6 ô chỉ nhận chữ số (`REGEXP_ONLY_DIGITS`, `maxLength=6`). |
| Mobile | Hiển thị `pickupCode` do Backend trả về, không tự sinh hay đổi định dạng. |

Mã dùng một lần và gắn với locker hiện tại: `POST /api/return-pickup-sessions` tìm theo cặp
(mã, locker) và chỉ chấp nhận khi yêu cầu đang ở trạng thái `Deposited`.

Hằng số `PickupCodeLength` trong `ReturnRequestService` được dùng chung cho sinh và kiểm tra mã, để hai nơi không lệch nhau.
