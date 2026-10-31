# ☕ BabyBus - Hệ Thống Quản Lý & Bán Hàng Quán Cafe (POS & Mobile Ecosystem)

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/Platform-Windows%20WPF-brightgreen.svg)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Android](https://img.shields.io/badge/Android-Native%20(Java)-3DDC84.svg?logo=android)](https://developer.android.com/)
[![SQL Server](https://img.shields.io/badge/Database-SQL%20Server-CC292B.svg?logo=microsoft-sql-server)](https://www.microsoft.com/sql-server)

**BabyBus** là giải pháp phần mềm quản lý và bán hàng quán cà phê toàn diện, chuyên nghiệp, hỗ trợ đa thiết bị hoạt động đồng bộ thời gian thực qua mạng nội bộ LAN / Wi-Fi:
* 🖥️ **Máy tính Thu ngân / Máy chủ POS**: Ứng dụng Desktop WPF (.NET 8) hiệu năng cao, tích hợp Web Server Kestrel máy chủ API.
* 🍳 **Máy tính Bếp / Pha chế**: Màn hình quản lý bếp nhận món theo thời gian thực (SSE - Server-Sent Events) và hỗ trợ in phiếu bếp theo đợt.
* 📱 **Thiết bị Di động (Phục vụ / Order tại bàn)**: Ứng dụng Android Native và giao diện Web Mobile trực quan cho nhân viên order ngay tại bàn khách.

---

## 📐 Kiến Trúc Hệ Thống (System Architecture)

```
                            ┌─────────────────────────────────────────────────────────┐
                            │            MÁY 1: MÁY CHỦ THU NGÂN / POS CHÍNH          │
                            │  • Ứng dụng Desktop WPF (.NET 8)                        │
                            │  • Cơ sở dữ liệu: Microsoft SQL Server (Port 1433)      │
                            │  • Máy chủ Web Kestrel tích hợp (Port 5000)             │
                            └────────────────────────────┬────────────────────────────┘
                                                         │
                                    Mạng Nội Bộ LAN / Wi-Fi Quán Cafe
                                                         │
                         ┌───────────────────────────────┴───────────────────────────────┐
                         ▼                                                               ▼
        ┌───────────────────────────────────┐                         ┌─────────────────────────────────────┐
        │       MÁY 2: MÁY BẾP / MÁY PHỤ    │                         │  ĐIỆN THOẠI DI ĐỘNG (ORDER TẠI BÀN) │
        │  • Chạy ứng dụng CafeManagement   │                         │  • App Android Native (Java/Gradle) │
        │  • Đọc/Ghi dữ liệu SQL Server     │                         │  • Web Mobile (order.html)          │
        │  • Nhận thông báo Realtime (SSE)  │                         │  • Kết nối REST API Cổng 5000       │
        └───────────────────────────────────┘                         └─────────────────────────────────────┘
```

---

## 🗂️ Cấu Trúc Thư Mục Dự Án

| Thư mục | Nền tảng | Mô tả |
| :--- | :--- | :--- |
| [**`CafeManagement/`**](./CafeManagement) | .NET 8 (WPF + Kestrel) | Ứng dụng Desktop POS chính, máy chủ cơ sở dữ liệu SQL Server, Web API và Web Client |
| [**`CafeMobileApp/`**](./CafeMobileApp) | Android Native (Java) | Ứng dụng Android dành riêng cho nhân viên phục vụ order tại bàn |
| **`CafeManagement/wwwroot/`** | HTML5 / CSS3 / JS | Giao diện Web Mobile Order đa nền tảng (chạy trực tiếp trên trình duyệt iOS / Android) |

---

## ⚡ Tính Năng Nổi Bật

### 1. Bán hàng & Thu ngân (POS Desktop)
* **Giao diện trực quan**: Sơ đồ phòng bàn hiển thị màu sắc trạng thái (Bàn trống, Bàn đang có khách, Bàn đã in tạm tính), thời gian khách ngồi và tổng tiền tức thì.
* **Cơ chế chống nghẽn giờ cao điểm**: Áp dụng **Session Guard Token** và các chỉ mục CSDL **Non-Clustered Indexes**, đảm bảo chuyển bàn siêu nhanh trên cả máy tính cũ (Core i7 thế hệ 2).
* **Bảo vệ ghi chú thông minh**: Tự động nhận diện khi thu ngân đang gõ ghi chú để tránh cướp focus hay mất dữ liệu khi điện thoại đồng bộ đơn mới.
* **Quản lý thực đơn linh hoạt**: Tìm kiếm sản phẩm theo tên không dấu / có dấu, phân loại danh mục, nhập/xuất thực đơn tự động bằng file Excel (`.xlsx`).

### 2. Màn hình Bếp / Pha chế (Kitchen View)
* **Cập nhật Realtime không độ trễ**: Tự động nhận món mới ngay khi điện thoại bấm "Báo bếp" qua Server-Sent Events (SSE).
* **In phiếu bếp theo đợt (Batch Printing)**: Chỉ in đúng các món vừa làm xong của đợt mới, tuyệt đối không in lặp lại các món đợt cũ.
* **Thao tác 1 chạm**: Nút hoàn thành nhanh toàn bàn hoặc từng món với độ tương phản cao, dễ nhìn trong quầy pha chế.

### 3. Order Cầm Tay Tại Bàn (Mobile App & Web)
* **Phân loại thẻ màu trực quan**:
  * ⚪ **Trắng**: Món nháp mới gọi thêm chưa gửi bếp.
  * 🟢 **Xanh lá**: Món đang chờ pha chế trong bếp.
  * 🔴 **Đỏ**: Món bếp đã chế biến xong.
* **Gọi thêm & Bớt món thông minh**: Tự động tách dòng cho các đợt gọi thêm món cũ đã xong, hộp thoại xác nhận khi trả món.
* **Hỗ trợ Dark Mode**: Khóa cố định màu chữ đậm rõ nét trên mọi giao diện và bàn phím điện thoại.

---

## 🛠️ Yêu Cầu Hệ Thống & Cài Đặt

### Yêu cầu tiên quyết:
* **Hệ điều hành**: Windows 10 / Windows 11 (64-bit)
* **Môi trường chạy**: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
* **Cơ sở dữ liệu**: Microsoft SQL Server (LocalDB, SQL Server Express hoặc Standard)
* **Môi trường build Mobile**: Android Studio Iguana+, JDK 17+, Android SDK 34

---

## 🚀 Hướng Dẫn Khởi Chạy Nhanh

### 1. Khởi chạy Ứng dụng Desktop POS:
```bash
# Di chuyển vào thư mục CafeManagement
cd CafeManagement

# Build và chạy ứng dụng
dotnet build
dotnet run
```

### 2. Đóng gói Bản Xuất Bản (Publish Release):
```bash
dotnet publish -c Release -o "C:\Users\admin\Desktop\PhanMemQuanCafe"
```

### 3. Build File Cài Đặt Android APK:
```bash
cd CafeMobileApp
./gradlew assembleDebug
# File APK đầu ra: CafeMobileApp/app/build/outputs/apk/debug/app-debug.apk
```

---

## 🌐 Hướng Dẫn Kết Nối Mạng 2 Máy & Điện Thoại

1. **Trên Máy 1 (Máy chủ)**:
   * Chạy file `2-BAT_MANG_SQL_SERVER_MAY_PHU.bat` bằng quyền Administrator để mở cổng CSDL 1433 và kích hoạt tài khoản `sa`.
   * Chạy file `1-MO_TUONG_LUA_CHO_DIEN_THOAI.bat` để mở cổng 5000 cho điện thoại kết nối.
2. **Trên Máy 2 (Máy phụ / Bếp)**:
   * Cài đặt IP Máy 1 trong file `appsettings.json`:
     ```json
     {
       "ConnectionStrings": {
         "DefaultConnection": "Server=tcp:<IP_MÁY_1>,1433;Database=CafeManagement;User Id=sa;Password=123456;TrustServerCertificate=True;"
       }
     }
     ```
3. **Trên Điện thoại**:
   * Cùng kết nối vào Wi-Fi quán, nhập địa chỉ IP: `http://<IP_MÁY_1>:5000`.


