package com.cafemanagement.orderapp.adapters;

import android.graphics.Color;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import com.cafemanagement.orderapp.R;
import com.cafemanagement.orderapp.models.CategoryItem;

import java.util.ArrayList;
import java.util.List;

public class CategoryAdapter extends RecyclerView.Adapter<CategoryAdapter.ViewHolder> {

    public interface OnCategoryClickListener {
        void onCategoryClick(CategoryItem category);
    }

    private List<CategoryItem> categories = new ArrayList<>();
    private int selectedCategoryId = 0;
    private OnCategoryClickListener listener;

    public CategoryAdapter(OnCategoryClickListener listener) {
        this.listener = listener;
    }

    public void setCategories(List<CategoryItem> categories, int selectedId) {
        this.categories = categories != null ? categories : new ArrayList<>();
        this.selectedCategoryId = selectedId;
        notifyDataSetChanged();
    }

    public void setSelectedCategoryId(int id) {
        this.selectedCategoryId = id;
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_category, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        CategoryItem cat = categories.get(position);
        holder.tvName.setText(cat.name);

        if (cat.id == selectedCategoryId) {
            holder.tvName.setBackgroundResource(R.drawable.bg_tab_selected);
            holder.tvName.setTextColor(Color.WHITE);
        } else {
            holder.tvName.setBackgroundResource(R.drawable.bg_tab_unselected);
            holder.tvName.setTextColor(Color.parseColor("#475569"));
        }

        holder.itemView.setOnClickListener(v -> {
            selectedCategoryId = cat.id;
            notifyDataSetChanged();
            if (listener != null) listener.onCategoryClick(cat);
        });
    }

    @Override
    public int getItemCount() {
        return categories.size();
    }

    static class ViewHolder extends RecyclerView.ViewHolder {
        TextView tvName;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = (TextView) itemView;
        }
    }
}
