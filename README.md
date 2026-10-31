# BabyBus - Hệ Thống Quản Lý & Bán Hàng Quán Cafe (POS & Mobile App)

Hệ thống quản lý quán cà phê toàn diện hỗ trợ đa thiết bị hoạt động thông suốt qua mạng LAN / Wi-Fi nội bộ:
1. **Máy tính Thu ngân / POS chính**: Ứng dụng Desktop WPF (.NET 8) kết hợp Web Server Kestrel tích hợp.
2. **Máy tính Bếp / Pha chế**: Đồng bộ trạng thái đơn hàng thời gian thực (SSE - Server-Sent Events).
3. **Thiết bị Di động (Phục vụ / Order)**: Ứng dụng Android Native và Web Mobile App cho phép nhân viên order trực tiếp tại bàn.

---

## 🏗️ Cấu Trúc Dự Án

- **`CafeManagement/`**: Ứng dụng Desktop POS viết bằng WPF (.NET 8), tích hợp CSDL Microsoft SQL Server và máy chủ Web Server API Kestrel phục vụ thiết bị di động.
- **`CafeMobileApp/`**: Ứng dụng Android Native viết bằng Java/Gradle phục vụ nhân viên order cầm tay tại bàn.
- **`CafeManagement/wwwroot/`**: Giao diện Web Mobile Order phục vụ truy cập trực tiếp từ trình duyệt trên mọi thiết bị.

---

## ⚡ Tính Năng Nổi Bật

- **Đồng bộ thời gian thực 2 chiều**: Cập nhật trạng thái gọi món, báo bếp, tạm tính và thanh toán tức thì qua mạng LAN.
- **Quản lý phòng bàn & thực đơn trực quan**: Xem trạng thái bàn trống / đang có khách, thời gian khách ngồi, tổng tiền thời gian thực.
- **Hỗ trợ in ấn linh hoạt**: In tạm tính, in hóa đơn thanh toán và in phiếu bếp theo đợt (Batch Printing) qua máy in nhiệt POS 80mm/58mm.
- **Tối ưu hóa hiệu năng cao điểm**: Sử dụng Session Guard, chỉ mục CSDL Non-Clustered Indexes và cơ chế chống dồn sự kiện (Debounce), chạy mượt mà trên cả máy tính cấu hình cũ.
- **Bảo mật & Quản trị**: Phân quyền thu ngân / quản trị viên, nhập xuất thực đơn tự động bằng Excel.

---

## 🚀 Công Nghệ Sử Dụng

- **Backend / POS Desktop**: C#, WPF, .NET 8, Microsoft SQL Server, Microsoft.Data.SqlClient, ClosedXML.
- **Web API & Realtime**: ASP.NET Core Kestrel, Server-Sent Events (SSE).
- **Mobile Client**: Android SDK (Java), Retrofit/OkHttp, RecyclerView.
- **Web Client**: HTML5, CSS3, Vanilla JavaScript (Zero Dependencies).
