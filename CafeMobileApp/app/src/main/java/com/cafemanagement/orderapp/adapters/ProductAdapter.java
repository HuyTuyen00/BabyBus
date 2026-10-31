package com.cafemanagement.orderapp.adapters;

import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ImageButton;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import com.cafemanagement.orderapp.R;
import com.cafemanagement.orderapp.models.ProductItem;

import java.text.DecimalFormat;
import java.util.ArrayList;
import java.util.List;

public class ProductAdapter extends RecyclerView.Adapter<ProductAdapter.ViewHolder> {

    public interface OnProductClickListener {
        void onProductAdd(ProductItem product);
    }

    private List<ProductItem> products = new ArrayList<>();
    private OnProductClickListener listener;
    private DecimalFormat fmt = new DecimalFormat("#,###");

    public ProductAdapter(OnProductClickListener listener) {
        this.listener = listener;
    }

    public void setProducts(List<ProductItem> products) {
        this.products = products != null ? products : new ArrayList<>();
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_product, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        ProductItem p = products.get(position);
        holder.tvName.setText(p.name);
        holder.tvPrice.setText(fmt.format(p.price) + " đ");

        holder.btnAdd.setOnClickListener(v -> {
            if (listener != null) listener.onProductAdd(p);
        });

        holder.itemView.setOnClickListener(v -> {
            if (listener != null) listener.onProductAdd(p);
        });
    }

    @Override
    public int getItemCount() {
        return products.size();
    }

    static class ViewHolder extends RecyclerView.ViewHolder {
        TextView tvName, tvPrice;
        ImageButton btnAdd;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = itemView.findViewById(R.id.tvProductName);
            tvPrice = itemView.findViewById(R.id.tvProductPrice);
            btnAdd = itemView.findViewById(R.id.btnAddProduct);
        }
    }
}
