# 🖥️ CafeManagement - Máy Chủ POS & Màn Hình Bán Hàng Desktop

Ứng dụng Desktop POS chính của hệ thống **BabyBus**, được xây dựng trên nền tảng **C# / WPF (.NET 8)** tích hợp Web Server API Kestrel chạy ngầm phục vụ toàn bộ các thiết bị di động trong quán cà phê.

---

## 📦 Thành Phần Công Nghệ

- **Giao diện**: Windows Presentation Foundation (WPF), XAML, Modern UI Flat Theme.
- **Backend Framework**: .NET 8.0 Windows Desktop SDK.
- **Web API Tích Hợp**: Microsoft.AspNetCore.App (Kestrel Server cổng 5000).
- **Cơ Sở Dữ Liệu**: Microsoft SQL Server qua thư viện `Microsoft.Data.SqlClient` (v5.2.2).
- **Đọc / Xuất Excel**: `ClosedXML` (v0.102.3).
- **Xử lý In Ấn**: Win32 GDI+ Printing (máy in hóa đơn nhiệt ESC/POS 58mm và 80mm).

---

## 🛠️ Cấu Hình Kết Nối CSDL (`appsettings.json`)

### 1. Cấu hình trên Máy Chủ (Máy 1):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=CafeManagement;Integrated Security=True;TrustServerCertificate=True;"
  }
}
```

### 2. Cấu hình trên Máy Phụ (Máy 2 / Bếp):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:192.168.1.50,1433;Database=CafeManagement;User Id=sa;Password=123456;TrustServerCertificate=True;"
  }
}
```

---

## 📡 Tài Liệu REST API (Kestrel Server - Port 5000)

Khi ứng dụng `CafeManagement.exe` khởi chạy, nó tự động mở máy chủ Web API tại cổng **`5000`** phục vụ các thiết bị di động trong cùng mạng LAN:

### 1. Đồng Bộ Thời Gian Thực (SSE)
- **`GET /events`**: Server-Sent Events stream đẩy tín hiệu trạng thái (`update`, `new_order`, `completed`, `temp_printed`) tới client ngay tức thì.

### 2. Quản Lý Bếp & Món Chế Biến
- **`GET /api/kitchen/pending`**: Lấy danh sách các món đang chờ chế biến trong bếp.
- **`POST /api/kitchen/complete/{id}`**: Đánh dấu món đã pha chế xong.

### 3. API Dành Cho Ứng Dụng Mobile Order
- **`GET /api/mobile/tables`**: Lấy danh sách toàn bộ phòng/bàn và trạng thái sử dụng.
- **`GET /api/mobile/categories`**: Lấy danh sách nhóm thực đơn.
- **`GET /api/mobile/products`**: Lấy toàn bộ món ăn, đồ uống kèm giá và ảnh.
- **`GET /api/mobile/cart/{tableId}`**: Lấy giỏ hàng chi tiết của một bàn theo mã bàn.
- **`POST /api/mobile/order`**: Gửi đơn gọi món mới hoặc đợt gọi thêm từ điện thoại lên hệ thống.
- **`POST /api/mobile/update-quantity`**: Điều chỉnh tăng / giảm số lượng món đã gọi.
- **`POST /api/mobile/delete-item`**: Trả món / Xóa món khỏi đơn hàng.

---

## 🚀 Lệnh Build & Đóng Gói (CLI)

```bash
# Khôi phục gói thư viện NuGet
dotnet restore

# Build dự án ở chế độ Release
dotnet build -c Release

# Đóng gói xuất bản ra thư mục cài đặt
dotnet publish -c Release -o "C:\Users\admin\Desktop\PhanMemQuanCafe"
```
