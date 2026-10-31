package com.cafemanagement.orderapp.adapters;

import android.graphics.Color;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.cardview.widget.CardView;
import androidx.recyclerview.widget.RecyclerView;

import com.cafemanagement.orderapp.R;
import com.cafemanagement.orderapp.models.TableItem;

import java.text.DecimalFormat;
import java.util.ArrayList;
import java.util.List;

public class TableAdapter extends RecyclerView.Adapter<TableAdapter.ViewHolder> {

    public interface OnTableClickListener {
        void onTableClick(TableItem table);
    }

    private List<TableItem> tableList = new ArrayList<>();
    private OnTableClickListener listener;
    private DecimalFormat fmt = new DecimalFormat("#,###");

    public TableAdapter(OnTableClickListener listener) {
        this.listener = listener;
    }

    public void setTables(List<TableItem> tables) {
        this.tableList = tables != null ? tables : new ArrayList<>();
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_table, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        TableItem table = tableList.get(position);
        holder.tvName.setText(table.name);

        if (table.hasPrintedBill) {
            holder.layoutBg.setBackgroundColor(Color.parseColor("#fef08a"));
            holder.tvName.setTextColor(Color.parseColor("#854d0e"));
            holder.tvSub.setText(!table.occupiedTimeText.isEmpty() ? table.occupiedTimeText + " • Đã in bill" : "Đã in bill");
            holder.tvTotal.setText(fmt.format(table.totalAmount) + " đ");
            holder.tvTotal.setTextColor(Color.parseColor("#854d0e"));
        } else if (table.isOccupied) {
            holder.layoutBg.setBackgroundColor(Color.parseColor("#dbeafe"));
            holder.tvName.setTextColor(Color.parseColor("#1e40af"));
            String sub = table.occupiedTimeText;
            if (table.itemCount > 0) {
                sub += (!sub.isEmpty() ? " • " : "") + table.itemCount + " món";
            }
            holder.tvSub.setText(sub);
            holder.tvTotal.setText(fmt.format(table.totalAmount) + " đ");
            holder.tvTotal.setTextColor(Color.parseColor("#1e40af"));
        } else {
            holder.layoutBg.setBackgroundColor(Color.parseColor("#ffffff"));
            holder.tvName.setTextColor(Color.parseColor("#0f172a"));
            holder.tvSub.setText("Còn trống");
            holder.tvTotal.setText("");
        }

        holder.itemView.setOnClickListener(v -> {
            if (listener != null) listener.onTableClick(table);
        });
    }

    @Override
    public int getItemCount() {
        return tableList.size();
    }

    static class ViewHolder extends RecyclerView.ViewHolder {
        LinearLayout layoutBg;
        TextView tvName, tvSub, tvTotal;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            layoutBg = itemView.findViewById(R.id.layoutTableBg);
            tvName = itemView.findViewById(R.id.tvTableName);
            tvSub = itemView.findViewById(R.id.tvTableSub);
            tvTotal = itemView.findViewById(R.id.tvTableTotal);
        }
    }
}
