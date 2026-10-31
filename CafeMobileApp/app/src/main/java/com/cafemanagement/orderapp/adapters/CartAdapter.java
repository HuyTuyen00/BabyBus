package com.cafemanagement.orderapp.adapters;

import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import com.cafemanagement.orderapp.R;
import com.cafemanagement.orderapp.models.CartItem;

import java.text.DecimalFormat;
import java.util.ArrayList;
import java.util.List;

public class CartAdapter extends RecyclerView.Adapter<CartAdapter.ViewHolder> {

    public interface OnCartItemChangeListener {
        void onQuantityChanged(CartItem item, int newQty);
        void onAddNewRound(CartItem item);
        void onNoteChanged(CartItem item, String newNote);
        void onItemDeleted(CartItem item);
    }

    private List<CartItem> cartItems = new ArrayList<>();
    private OnCartItemChangeListener listener;
    private DecimalFormat fmt = new DecimalFormat("#,###");

    public CartAdapter(OnCartItemChangeListener listener) {
        this.listener = listener;
    }

    public void setItems(List<CartItem> items) {
        this.cartItems = items != null ? items : new ArrayList<>();
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_cart, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        CartItem item = cartItems.get(position);
        holder.tvName.setText(item.productName);
        holder.tvPrice.setText(fmt.format(item.getTotalPrice()) + " đ");
        holder.tvQty.setText(String.valueOf(item.quantity));

        // Inline note EditText with safe listener detaching
        if (holder.noteWatcher != null) {
            holder.edtNote.removeTextChangedListener(holder.noteWatcher);
        }
        String currentText = holder.edtNote.getText().toString();
        String itemNote = item.note != null ? item.note : "";
        if (!currentText.equals(itemNote)) {
            holder.edtNote.setText(itemNote);
        }

        holder.noteWatcher = new TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {}

            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {}

            @Override
            public void afterTextChanged(Editable s) {
                String newNote = s.toString();
                item.note = newNote;
                item.isSaved = false;
                if (listener != null) {
                    listener.onNoteChanged(item, newNote);
                }
            }
        };
        holder.edtNote.addTextChangedListener(holder.noteWatcher);

        applyCardStyle(holder, item);

        holder.btnMinus.setOnClickListener(v -> {
            if (listener != null) listener.onQuantityChanged(item, item.quantity - 1);
        });

        holder.btnPlus.setOnClickListener(v -> {
            if (item.isCompleted()) {
                // Đồ đã chế biến xong (Màu Đỏ): Bấm (+) sẽ tạo dòng mới (Màu Trắng) riêng biệt!
                if (listener != null) listener.onAddNewRound(item);
            } else {
                if (listener != null) listener.onQuantityChanged(item, item.quantity + 1);
            }
        });

        holder.btnDelete.setOnClickListener(v -> {
            if (listener != null) listener.onItemDeleted(item);
        });
    }

    private void applyCardStyle(ViewHolder holder, CartItem item) {
        float density = holder.itemView.getContext().getResources().getDisplayMetrics().density;
        int strokeWidth = Math.round(1.5f * density);
        int cornerRadius = Math.round(8f * density);

        if (holder.layoutCartItem != null) {
            GradientDrawable gd = new GradientDrawable();
            gd.setShape(GradientDrawable.RECTANGLE);
            gd.setCornerRadius(cornerRadius);

            // Minus, Plus, and Delete buttons are ALWAYS visible and active
            holder.btnDelete.setVisibility(View.VISIBLE);
            holder.btnMinus.setVisibility(View.VISIBLE);
            holder.btnPlus.setVisibility(View.VISIBLE);
            holder.edtNote.setEnabled(true);
            holder.edtNote.setFocusable(true);
            holder.edtNote.setFocusableInTouchMode(true);

            if (item.isCompleted()) {
                // 🔴 ĐÃ CHẾ BIẾN XONG / MÓN KHÔNG BÁO BẾP (Completed / Served): Màu đỏ hồng
                gd.setColor(Color.parseColor("#FEE2E2")); // Soft red / pink
                gd.setStroke(strokeWidth, Color.parseColor("#FCA5A5")); // Red border
                holder.layoutCartItem.setBackground(gd);
            } else if (item.isSaved) {
                // 🟢 ĐÃ BÁO BẾP (Pending / In kitchen): Màu xanh lá nhạt
                gd.setColor(Color.parseColor("#E8F5E9"));
                gd.setStroke(strokeWidth, Color.parseColor("#A7F3D0"));
                holder.layoutCartItem.setBackground(gd);
            } else {
                // ⚪ CHƯA BÁO BẾP / GỌI THÊM MỚI (Draft): Màu trắng, viền xám
                gd.setColor(Color.parseColor("#FFFFFF"));
                gd.setStroke(strokeWidth, Color.parseColor("#CBD5E1"));
                holder.layoutCartItem.setBackground(gd);
            }
        }
    }

    @Override
    public int getItemCount() {
        return cartItems.size();
    }

    static class ViewHolder extends RecyclerView.ViewHolder {
        View layoutCartItem;
        TextView tvName, tvPrice, tvQty;
        EditText edtNote;
        Button btnMinus, btnPlus;
        ImageButton btnDelete;
        TextWatcher noteWatcher;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            layoutCartItem = itemView.findViewById(R.id.layoutCartItem);
            tvName = itemView.findViewById(R.id.tvCartItemName);
            tvPrice = itemView.findViewById(R.id.tvCartItemPrice);
            edtNote = itemView.findViewById(R.id.edtCartItemNote);
            tvQty = itemView.findViewById(R.id.tvCartQty);
            btnMinus = itemView.findViewById(R.id.btnCartMinus);
            btnPlus = itemView.findViewById(R.id.btnCartPlus);
            btnDelete = itemView.findViewById(R.id.btnDeleteCartItem);
        }
    }
}
