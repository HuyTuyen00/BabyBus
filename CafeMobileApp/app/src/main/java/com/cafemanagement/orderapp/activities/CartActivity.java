package com.cafemanagement.orderapp.activities;

import android.app.AlertDialog;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import com.cafemanagement.orderapp.R;
import com.cafemanagement.orderapp.adapters.CartAdapter;
import com.cafemanagement.orderapp.api.ApiClient;
import com.cafemanagement.orderapp.models.CartItem;
import com.cafemanagement.orderapp.models.OrderRequest;

import java.text.DecimalFormat;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class CartActivity extends AppCompatActivity implements CartAdapter.OnCartItemChangeListener {

    private TextView tvTitle, tvTotalAmount;
    private ImageButton btnBack;
    private RecyclerView rvCartItems;
    private Button btnSubmitOrder;

    private CartAdapter cartAdapter;
    private DecimalFormat fmt = new DecimalFormat("#,###");

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_cart);

        initViews();
        setupListeners();
        renderCartItems();
    }

    private void initViews() {
        tvTitle = findViewById(R.id.tvCartTitle);
        tvTitle.setText("Đơn hàng - " + MenuActivity.currentTableName);

        btnBack = findViewById(R.id.btnBackCart);
        rvCartItems = findViewById(R.id.rvCartItems);
        tvTotalAmount = findViewById(R.id.tvCartTotalAmount);
        btnSubmitOrder = findViewById(R.id.btnSubmitOrder);

        rvCartItems.setLayoutManager(new LinearLayoutManager(this));
        cartAdapter = new CartAdapter(this);
        rvCartItems.setAdapter(cartAdapter);
    }

    private void setupListeners() {
        btnBack.setOnClickListener(v -> finish());
        btnSubmitOrder.setOnClickListener(v -> submitOrderToKitchen());
    }

    private void renderCartItems() {
        cartAdapter.setItems(MenuActivity.currentCart);
        updateTotalAmount();
    }

    private void updateTotalAmount() {
        double total = 0;
        for (CartItem item : MenuActivity.currentCart) {
            total += item.getTotalPrice();
        }
        tvTotalAmount.setText(fmt.format(total) + " đ");
    }

    @Override
    public void onQuantityChanged(CartItem item, int newQty) {
        if (newQty <= 0) {
            MenuActivity.currentCart.remove(item);
        } else {
            item.quantity = newQty;
            item.isSaved = false;
        }
        renderCartItems();
    }

    @Override
    public void onAddNewRound(CartItem item) {
        CartItem foundDraft = null;
        for (CartItem ci : MenuActivity.currentCart) {
            if (ci.productId == item.productId && !ci.isSaved) {
                foundDraft = ci;
                break;
            }
        }
        if (foundDraft != null) {
            foundDraft.quantity++;
        } else {
            CartItem newItem = new CartItem(item.productId, item.productName, 1, item.price, "", item.isExcludeFromKitchen);
            newItem.isSaved = false;
            MenuActivity.currentCart.add(newItem);
        }
        renderCartItems();
    }

    @Override
    public void onNoteChanged(CartItem item, String newNote) {
        item.note = newNote;
        item.isSaved = false;
    }

    @Override
    public void onItemDeleted(CartItem item) {
        MenuActivity.currentCart.remove(item);
        renderCartItems();
    }

    public void onNoteClicked(CartItem item) {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        View dialogView = LayoutInflater.from(this).inflate(R.layout.dialog_note, null);
        TextView tvDialogTitle = dialogView.findViewById(R.id.tvDialogNoteTitle);
        EditText edtNote = dialogView.findViewById(R.id.edtItemNote);

        tvDialogTitle.setText("Ghi chú: " + item.productName);
        edtNote.setText(item.note);

        builder.setView(dialogView)
                .setPositiveButton("Lưu", (dialog, which) -> {
                    item.note = edtNote.getText().toString().trim();
                    item.isSaved = false;
                    renderCartItems();
                })
                .setNegativeButton("Hủy", null)
                .show();
    }

    private void submitOrderToKitchen() {
        if (MenuActivity.currentCart.isEmpty()) {
            Toast.makeText(this, "Chưa có món nào để báo bếp!", Toast.LENGTH_SHORT).show();
            return;
        }

        btnSubmitOrder.setEnabled(false);
        btnSubmitOrder.setText("ĐANG GỬI BÁO BẾP...");

        OrderRequest req = new OrderRequest();
        req.tableId = MenuActivity.currentTableId;
        for (CartItem item : MenuActivity.currentCart) {
            req.items.add(new OrderRequest.OrderItem(item.id, item.productId, item.quantity, item.note));
        }

        new Thread(() -> {
            try {
                ApiClient.httpPostJson(this, "/api/mobile/order", req);
                runOnUiThread(() -> {
                    Toast.makeText(CartActivity.this, "Đã báo bếp thành công!", Toast.LENGTH_LONG).show();
                    btnSubmitOrder.setEnabled(true);
                    finish();
                });
            } catch (Exception e) {
                runOnUiThread(() -> {
                    btnSubmitOrder.setEnabled(true);
                    btnSubmitOrder.setText("THÔNG BÁO (BÁO BẾP)");
                    Toast.makeText(CartActivity.this, "Lỗi gửi báo bếp: " + e.getMessage(), Toast.LENGTH_LONG).show();
                });
            }
        }).start();
    }
}
