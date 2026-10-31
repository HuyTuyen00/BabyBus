# HƯỚNG DẪN CẤU HÌNH KẾT NỐI DÙNG CHUNG CSDL SQL SERVER CHO 2 LAPTOP QUA WI-FI

Tài liệu này hướng dẫn chi tiết cách thiết lập **Máy 1 (Máy chủ chứa CSDL)** và **Máy 2 (Laptop bên cạnh - Máy phụ)** để cả 2 máy cùng chạy ứng dụng **CafeManagement** và đồng bộ chung dữ liệu hóa đơn, sản phẩm, bàn ăn qua cùng mạng Wi-Fi.

---

## I. TỔNG QUAN NGUYÊN LÝ KẾT NỐI

```
  +-----------------------------------+             +-----------------------------------+
  |   MÁY 1 (MÁY CHỦ CHỨA DATA)       |             |     MÁY 2 (LAPTOP BÊN CẠNH)       |
  |  - IP Wi-Fi: 192.168.x.x           |             |  - Chạy CafeManagement.exe        |
  |  - Chạy SQL Server Express (1433) | <== Wi-Fi = |  - Cấu hình SQL Server IP:        |
  |  - Chạy CafeManagement.exe        |    (LAN)    |    192.168.x.x,1433                |
  +-----------------------------------+             +-----------------------------------+
```

1. **Máy 1 (Máy chính)**: Đóng vai trò SQL Server chứa dữ liệu. IP mạng Wi-Fi của Máy 1 (Ví dụ: `192.168.6.102`).
2. **Máy 2 (Máy phụ - Laptop bên cạnh)**: Cài ứng dụng `CafeManagement.exe`, mở **Cài đặt** và nhập Địa chỉ Máy chủ SQL: `192.168.6.102` (hoặc `192.168.6.102,1433`).

---

## II. BƯỚC 1: THIẾT LẬP TRÊN MÁY 1 (MÁY CHỦ CHỨA CSDL)

### 1. Kiểm tra / Cài đặt SQL Server Express
LocalDB mặc định (`(localdb)\mssqllocaldb`) chỉ cho phép ứng dụng trên chính máy đó kết nối. Để **Laptop khác kết nối qua Wi-Fi**, Máy 1 cần chạy **SQL Server Express** (bản miễn phí của Microsoft):
- Tải SQL Server Express (nếu máy chưa có): [Download Microsoft SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads)
- Trong quá trình cài đặt, chọn kiểu **Basic** hoặc **Custom**, đặt tên Instance là `SQLEXPRESS` và bật chế độ **Mixed Mode Authentication** (Đặt mật khẩu cho tài khoản `sa`, ví dụ `123456`).

### 2. Bật giao thức TCP/IP trong SQL Server Configuration Manager
1. Bấm `Win + R`, nhập `sqlservermanager16.msc` (hoặc tìm **SQL Server Configuration Manager**).
2. Chọn **SQL Server Network Configuration** -> **Protocols for SQLEXPRESS**.
3. Nhấp đúp vào **TCP/IP** -> Chuyển **Enabled** thành **Yes**.
4. Chuyển sang thẻ **IP Addresses**, kéo xuống mục **IPAll**:
   - Xóa dòng tại *TCP Dynamic Ports*.
   - Nhập `1433` vào ô **TCP Port**.
5. Bấm **OK**, sau đó vào mục **SQL Server Services** -> Nhấp chuột phải vào **SQL Server (SQLEXPRESS)** -> Chọn **Restart**.

### 3. Mở Cổng Tường Lửa (Windows Firewall) cho Máy 1
Chạy file script tự động trong thư mục dự án:
1. Nhấp chuột phải vào file [Configure-SQLFirewall.ps1](file:///c:/Users/admin/Desktop/BabyBus/CafeManagement/Configure-SQLFirewall.ps1).
2. Chọn **Run with PowerShell** (hoặc mở PowerShell dưới quyền Administrator và chạy `.\Configure-SQLFirewall.ps1`).
3. Script sẽ tự động cho phép cổng `1433` (SQL Server) và `5000` (Màn hình bếp WebServer) đi qua Tường lửa.

### 4. Lấy địa chỉ IP Wi-Fi của Máy 1
Mở phần mềm **CafeManagement** trên Máy 1 -> Chọn menu **Cài đặt**:
- Tại mục **Cấu hình Kết nối CSDL SQL Server**, hệ thống hiển thị sẵn **IP Máy này (trong mạng Wi-Fi)** (Ví dụ: `192.168.6.102`).
- Bấm nút **📋 Sao chép IP** để gửi/lưu lại IP này.

---

## III. BƯỚC 2: THIẾT LẬP TRÊN MÁY 2 (LAPTOP BÊN CẠNH - MÁY PHỤ)

1. Copy thư mục phần mềm **CafeManagement** sang Máy 2 (Laptop bên cạnh).
2. Kết nối Máy 2 vào **cùng mạng Wi-Fi** với Máy 1.
3. Mở phần mềm `CafeManagement.exe` trên Máy 2.
4. Mở phần **Cài đặt** -> kéo xuống mục **🗄️ Cấu hình Kết nối CSDL SQL Server**:
   - **Địa chỉ Máy chủ SQL (Server / IP)**: Nhập IP Máy 1 (Ví dụ: `192.168.6.102` hoặc `192.168.6.102\SQLEXPRESS`).
   - **Tên Cơ sở dữ liệu**: `CafeManagement`
   - **Phương thức xác thực**:
     - Nếu dùng SQL Authentication: Chọn **Tài khoản SQL Server**, nhập Tên đăng nhập (`sa`) và Mật khẩu (VD: `123456`).
     - Nếu dùng Windows Auth (cùng mạng domain/workgroup): Chọn **Windows Authentication**.
5. Bấm **🔌 Kiểm tra kết nối SQL**:
   - Nếu báo `✅ Kết nối SQL thành công!`, bấm nút **💾 Lưu cấu hình SQL**.
6. Từ lúc này, Máy 2 (Laptop bên cạnh) sẽ đọc/ghi dữ liệu trực tiếp về CSDL trên Máy 1 thành công!

---

## IV. BẢNG TÓM TẮT THÔNG SỐ CẤU HÌNH

| Thông số | Giá trị trên Máy 1 (Máy chủ) | Giá trị trên Máy 2 (Laptop phụ) |
| :--- | :--- | :--- |
| **Server / IP** | `(localdb)\mssqllocaldb` hoặc `.\SQLEXPRESS` | `<IP_MÁY_1>` (VD: `192.168.6.102`) |
| **Cổng kết nối SQL** | `1433` (Đã mở Firewall) | `1433` |
| **Database Name** | `CafeManagement` | `CafeManagement` |
| **Mạng kết nối** | Wi-Fi / LAN nội bộ | Cùng mạng Wi-Fi với Máy 1 |

---

## V. XỬ LÝ SỰ CỐ THƯỜNG GẶP

1. **Máy 2 báo "Lỗi kết nối SQL Server"**:
   - Kiểm tra xem 2 máy đã bắt chung 1 mạng Wi-Fi chưa.
   - Kiểm tra ping từ Máy 2 sang Máy 1: Mở Command Prompt trên Máy 2 gõ `ping 192.168.6.102`.
   - Đảm bảo đã chạy script `Configure-SQLFirewall.ps1` trên Máy 1 để mở cổng 1433.
   - Kiểm tra dịch vụ `SQL Server (SQLEXPRESS)` trên Máy 1 đã **Running** chưa.

2. **Địa chỉ IP của Máy 1 bị đổi khi khởi động lại Wi-Fi**:
   - Trong router Wi-Fi, chọn chức năng **DHCP IP Reservation** để gán cố định IP cho Máy 1 (ví dụ cố định là `192.168.6.102`), giúp Máy 2 không bao giờ phải chỉnh lại IP.
