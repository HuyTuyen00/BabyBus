# 📱 CafeMobileApp - Ứng Dụng Order Di Động Android

Ứng dụng Android Native dành riêng cho nhân viên phục vụ, cho phép gọi món, thêm bớt số lượng, ghi chú món và kiểm tra hóa đơn ngay tại bàn khách trong quán cà phê.

---

## 🛠️ Công Nghệ Phát Triển

- **Ngôn ngữ**: Java (JDK 17)
- **Nền tảng**: Android SDK (Compile SDK 34, Min SDK 24 - Android 7.0+)
- **Build Tool**: Gradle 8.11, Android Gradle Plugin 8.7.2
- **Thư viện Mạng**: Retrofit 2, OkHttp 3, Gson
- **Giao diện**: Material Components, RecyclerView, CardView, ViewBinding

---

## 🌟 Chức Năng Chính

1. **Sơ Đồ Phòng Bàn Nhanh Chóng**:
   - Hiển thị trực quan danh sách bàn với các trạng thái màu: Bàn trống, Bàn đang dùng, Bàn đã in bill tạm tính.
   - Tìm kiếm nhanh phòng/bàn theo tên không dấu hoặc có dấu.
2. **Thực Đơn & Giỏ Hàng Thông Minh**:
   - Lọc món theo từng danh mục hoặc tìm kiếm nhanh bằng ô search.
   - Hiển thị ảnh, giá và nút thêm nhanh.
3. **Phân Biệt Trạng Thái Món Bằng Màu Sắc**:
   - ⚪ **Trắng**: Món mới thêm vào giỏ chưa báo bếp.
   - 🟢 **Xanh lá**: Món đang trong hàng đợi bếp chờ chế biến.
   - 🔴 **Đỏ**: Món bếp đã hoàn thành mang ra bàn.
4. **Gọi Thêm & Trả Món An Toàn**:
   - Khi gọi thêm món cũ đã xong, app tự động tách thành một dòng nháp mới màu trắng, không làm sai lệch số lượng đợt cũ.
   - Hộp thoại xác nhận trước khi hủy/bớt món.
5. **Ghi Chú Món**:
   - Hỗ trợ ghi chú chi tiết cho từng món (ví dụ: *ít đường, không đá, nhiều sữa*).

---

## 📲 Hướng Dẫn Cài Đặt & Cấu Hình IP Máy Chủ

1. **Cài Đặt APK**:
   - Chép file `CafeMobileApp.apk` vào điện thoại Android và tiến hành cài đặt.
2. **Cấu Hình Địa Chỉ Máy Chủ**:
   - Đảm bảo điện thoại kết nối vào **cùng mạng Wi-Fi** với Máy tính POS.
   - Tại màn hình chính của ứng dụng, bấm vào biểu tượng **Cài đặt** (hoặc nút Cấu hình IP).
   - Nhập địa chỉ IP của Máy 1 kèm cổng: `192.168.1.50:5000` (thay bằng IP thực tế của máy chủ).
   - Bấm **Lưu**, app sẽ tự động kết nối và nạp danh sách bàn từ máy chủ ngay lập tức.

---

## 🔨 Lệnh Build File APK từ Command Line

```bash
# Di chuyển vào thư mục CafeMobileApp
cd CafeMobileApp

# Cấp quyền thực thi và build bản Debug
./gradlew assembleDebug

# File APK tạo ra tại đường dẫn:
# app/build/outputs/apk/debug/app-debug.apk
```
