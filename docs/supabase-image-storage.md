# Lưu ảnh trên Supabase Storage

API hỗ trợ `POST /api/images` và endpoint tương thích `POST /api/uploads/images`.
Gửi multipart/form-data với trường `file`; kết quả thành công là HTTP 201 với
`{ "url": "..." }`. URL này dùng cho `ParcelImageUrl` hoặc `AvatarUrl` như trước.
Chỉ nhận JPG/JPEG, PNG, WEBP có MIME và chữ ký nhị phân phù hợp; giới hạn request
upload là 8 MiB (bao gồm phần bao multipart).

## Cấu hình

Tạo bucket `smart-locking-images` trong Supabase và bật **Public** để URL trả về
có thể đọc trực tiếp. Cấu hình giới hạn dung lượng và MIME của bucket phù hợp.
Public chỉ áp dụng cho việc đọc; khóa backend vẫn cần quyền upload.

Đặt các giá trị sau bằng environment variables hoặc .NET user secrets:

```text
SupabaseStorage__Url=https://<project-ref>.supabase.co
SupabaseStorage__ApiKey=<backend-service-role-key>
SupabaseStorage__BucketName=smart-locking-images
SupabaseStorage__Folder=parcels
```

Không lưu khóa thật vào source control hoặc gửi khóa cho frontend. `Folder` có
thể để trống để lưu tại gốc bucket. Tên ảnh được tạo bằng GUID; API trả URL công
khai của object vừa tải lên.

Thiếu URL hoặc API key thì API dùng local storage tại `wwwroot/uploads/images`.
Khi đã chọn Supabase, upload thất bại sẽ trả lỗi; không tự chuyển sang local.
Ảnh local cũ không được di chuyển tự động và vẫn được phục vụ như trước.

Tham khảo: [Supabase standard uploads](https://supabase.com/docs/guides/storage/uploads/standard-uploads)
và [public download URLs](https://supabase.com/docs/guides/storage/serving/downloads).
