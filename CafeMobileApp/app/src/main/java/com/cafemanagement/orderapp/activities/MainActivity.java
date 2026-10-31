package com.cafemanagement.orderapp.activities;

import android.app.AlertDialog;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;
import androidx.recyclerview.widget.GridLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import com.cafemanagement.orderapp.R;
import com.cafemanagement.orderapp.adapters.TableAdapter;
import com.cafemanagement.orderapp.api.ApiClient;
import com.cafemanagement.orderapp.models.TableItem;
import com.google.gson.Gson;
import com.google.gson.reflect.TypeToken;

import java.util.ArrayList;
import java.util.List;

public class MainActivity extends AppCompatActivity implements TableAdapter.OnTableClickListener {

    private EditText edtSearchTable;
    private TextView tabAll, tabOccupied, tabEmpty;
    private RecyclerView rvTables;
    private ImageButton btnConfigIp, btnRefresh;

    private TableAdapter tableAdapter;
    private List<TableItem> allTables = new ArrayList<>();
    private String currentFilter = "ALL"; // ALL, OCCUPIED, EMPTY
    private String currentSearch = "";

    // Real-time auto-refresh handler (every 3 seconds)
    private final Handler autoRefreshHandler = new Handler(Looper.getMainLooper());
    private final Runnable autoRefreshRunnable = new Runnable() {
        @Override
        public void run() {
            loadTablesFromServerSilent();
            autoRefreshHandler.postDelayed(this, 3000);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        initViews();
        setupListeners();
    }

    @Override
    protected void onResume() {
        super.onResume();
        loadTablesFromServer();
        autoRefreshHandler.postDelayed(autoRefreshRunnable, 3000);
    }

    @Override
    protected void onPause() {
        super.onPause();
        autoRefreshHandler.removeCallbacks(autoRefreshRunnable);
    }

    private void initViews() {
        edtSearchTable = findViewById(R.id.edtSearchTable);
        tabAll = findViewById(R.id.tabAll);
        tabOccupied = findViewById(R.id.tabOccupied);
        tabEmpty = findViewById(R.id.tabEmpty);
        rvTables = findViewById(R.id.rvTables);
        btnConfigIp = findViewById(R.id.btnConfigIp);
        btnRefresh = findViewById(R.id.btnRefreshTables);

        rvTables.setLayoutManager(new GridLayoutManager(this, 2));
        tableAdapter = new TableAdapter(this);
        rvTables.setAdapter(tableAdapter);
    }

    private void setupListeners() {
        btnConfigIp.setOnClickListener(v -> showConfigIpDialog());
        btnRefresh.setOnClickListener(v -> loadTablesFromServer());

        tabAll.setOnClickListener(v -> selectFilterTab("ALL"));
        tabOccupied.setOnClickListener(v -> selectFilterTab("OCCUPIED"));
        tabEmpty.setOnClickListener(v -> selectFilterTab("EMPTY"));

        edtSearchTable.addTextChangedListener(new TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {}

            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                currentSearch = s.toString().trim().toLowerCase();
                filterAndRenderTables();
            }

            @Override
            public void afterTextChanged(Editable s) {}
        });
    }

    private void selectFilterTab(String filter) {
        currentFilter = filter;
        tabAll.setBackgroundResource("ALL".equals(filter) ? R.drawable.bg_tab_selected : R.drawable.bg_tab_unselected);
        tabAll.setTextColor("ALL".equals(filter) ? 0xFFFFFFFF : 0xFF475569);

        tabOccupied.setBackgroundResource("OCCUPIED".equals(filter) ? R.drawable.bg_tab_selected : R.drawable.bg_tab_unselected);
        tabOccupied.setTextColor("OCCUPIED".equals(filter) ? 0xFFFFFFFF : 0xFF475569);

        tabEmpty.setBackgroundResource("EMPTY".equals(filter) ? R.drawable.bg_tab_selected : R.drawable.bg_tab_unselected);
        tabEmpty.setTextColor("EMPTY".equals(filter) ? 0xFFFFFFFF : 0xFF475569);

        filterAndRenderTables();
    }

    private void loadTablesFromServer() {
        new Thread(() -> {
            try {
                String json = ApiClient.httpGet(this, "/api/mobile/tables");
                List<TableItem> result = new Gson().fromJson(json, new TypeToken<List<TableItem>>(){}.getType());
                runOnUiThread(() -> {
                    allTables = result != null ? result : new ArrayList<>();
                    filterAndRenderTables();
                });
            } catch (Exception e) {
                runOnUiThread(() -> {
                    Toast.makeText(MainActivity.this, "Không thể kết nối máy thanh toán. Vui lòng kiểm tra lại IP!", Toast.LENGTH_LONG).show();
                });
            }
        }).start();
    }

    private void loadTablesFromServerSilent() {
        new Thread(() -> {
            try {
                String json = ApiClient.httpGet(this, "/api/mobile/tables");
                List<TableItem> result = new Gson().fromJson(json, new TypeToken<List<TableItem>>(){}.getType());
                if (result != null) {
                    runOnUiThread(() -> {
                        allTables = result;
                        filterAndRenderTables();
                    });
                }
            } catch (Exception ignored) {}
        }).start();
    }

    private void filterAndRenderTables() {
        List<TableItem> filtered = new ArrayList<>();
        for (TableItem t : allTables) {
            boolean matchSearch = currentSearch.isEmpty() || t.name.toLowerCase().contains(currentSearch);
            boolean matchStatus = true;
            if ("OCCUPIED".equals(currentFilter)) matchStatus = t.isOccupied;
            else if ("EMPTY".equals(currentFilter)) matchStatus = !t.isOccupied;

            if (matchSearch && matchStatus) {
                filtered.add(t);
            }
        }
        tableAdapter.setTables(filtered);
    }

    @Override
    public void onTableClick(TableItem table) {
        Intent intent = new Intent(this, MenuActivity.class);
        intent.putExtra("TABLE_ID", table.id);
        intent.putExtra("TABLE_NAME", table.name);
        startActivity(intent);
    }

    private void showConfigIpDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        View dialogView = LayoutInflater.from(this).inflate(R.layout.dialog_ip_config, null);
        EditText edtIp = dialogView.findViewById(R.id.edtServerIp);
        edtIp.setText(ApiClient.getServerIp(this));

        builder.setView(dialogView)
                .setPositiveButton("Lưu", (dialog, which) -> {
                    String newIp = edtIp.getText().toString().trim();
                    if (!newIp.isEmpty()) {
                        ApiClient.setServerIp(this, newIp);
                        Toast.makeText(this, "Đã lưu địa chỉ IP máy thanh toán!", Toast.LENGTH_SHORT).show();
                        loadTablesFromServer();
                    }
                })
                .setNegativeButton("Hủy", null)
                .show();
    }
}
