package com.cafemanagement.orderapp.activities;

import android.content.res.ColorStateList;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
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
import com.cafemanagement.orderapp.adapters.CategoryAdapter;
import com.cafemanagement.orderapp.adapters.ProductAdapter;
import com.cafemanagement.orderapp.api.ApiClient;
import com.cafemanagement.orderapp.models.CartItem;
import com.cafemanagement.orderapp.models.CategoryItem;
import com.cafemanagement.orderapp.models.OrderRequest;
import com.cafemanagement.orderapp.models.ProductItem;
import com.google.gson.Gson;
import com.google.gson.reflect.TypeToken;

import java.text.DecimalFormat;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class MenuActivity extends AppCompatActivity
        implements CategoryAdapter.OnCategoryClickListener,
        ProductAdapter.OnProductClickListener,
        CartAdapter.OnCartItemChangeListener {

    public static List<CartItem> currentCart = new ArrayList<>();
    public static int currentTableId = 0;
    public static String currentTableName = "";

    private TextView tvTitle;
    private ImageButton btnBack, btnToggleSearch;
    private EditText edtSearchProduct;
    private RecyclerView rvCategories, rvProducts;

    private TextView tvCartBadge, tvCartTotalAmount;
    private RecyclerView rvCartItems;
    private Button btnSubmitOrder;

    private CategoryAdapter categoryAdapter;
    private ProductAdapter productAdapter;
    private CartAdapter cartAdapter;

    private List<CategoryItem> categories = new ArrayList<>();
    private List<ProductItem> allProducts = new ArrayList<>();

    private int selectedCategoryId = 0;
    private String currentSearch = "";
    private DecimalFormat fmt = new DecimalFormat("#,###");

    private int initialServerCartCount = 0;
    private boolean isLocalModified = false;

    // Real-time auto refresh for cart items (every 2.5 seconds)
    private final Handler autoRefreshHandler = new Handler(Looper.getMainLooper());
    private final Runnable autoRefreshRunnable = new Runnable() {
        @Override
        public void run() {
            loadExistingCartFromServerSilent();
            autoRefreshHandler.postDelayed(this, 2500);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_menu);

        currentTableId = getIntent().getIntExtra("TABLE_ID", 0);
        currentTableName = getIntent().getStringExtra("TABLE_NAME");

        initViews();
        setupListeners();
        loadCategoriesAndProducts();
        loadExistingCartFromServer();
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateCartPanel();
        autoRefreshHandler.postDelayed(autoRefreshRunnable, 2500);
    }

    @Override
    protected void onPause() {
        super.onPause();
        autoRefreshHandler.removeCallbacks(autoRefreshRunnable);
    }

    private void initViews() {
        tvTitle = findViewById(R.id.tvMenuTitle);
        tvTitle.setText(currentTableName);

        btnBack = findViewById(R.id.btnBackMenu);
        btnToggleSearch = findViewById(R.id.btnToggleSearchProduct);
        edtSearchProduct = findViewById(R.id.edtSearchProduct);

        rvCategories = findViewById(R.id.rvCategories);
        rvProducts = findViewById(R.id.rvProducts);

        categoryAdapter = new CategoryAdapter(this);
        rvCategories.setAdapter(categoryAdapter);

        rvProducts.setLayoutManager(new LinearLayoutManager(this));
        productAdapter = new ProductAdapter(this);
        rvProducts.setAdapter(productAdapter);

        tvCartBadge = findViewById(R.id.tvCartBadge);
        tvCartTotalAmount = findViewById(R.id.tvCartTotalAmount);
        rvCartItems = findViewById(R.id.rvCartItems);
        btnSubmitOrder = findViewById(R.id.btnSubmitOrder);

        rvCartItems.setLayoutManager(new LinearLayoutManager(this));
        cartAdapter = new CartAdapter(this);
        rvCartItems.setAdapter(cartAdapter);
    }

    private void setupListeners() {
        btnBack.setOnClickListener(v -> finish());

        btnToggleSearch.setOnClickListener(v -> {
            View layoutSearch = findViewById(R.id.layoutProductSearch);
            if (layoutSearch.getVisibility() == View.VISIBLE) {
                layoutSearch.setVisibility(View.GONE);
                edtSearchProduct.setText("");
            } else {
                layoutSearch.setVisibility(View.VISIBLE);
                edtSearchProduct.requestFocus();
            }
        });

        edtSearchProduct.addTextChangedListener(new TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {}

            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                currentSearch = s.toString().trim().toLowerCase();
                filterAndRenderProducts();
            }

            @Override
            public void afterTextChanged(Editable s) {}
        });

        btnSubmitOrder.setOnClickListener(v -> submitOrderToKitchen());
    }

    private void loadCategoriesAndProducts() {
        new Thread(() -> {
            try {
                String catJson = ApiClient.httpGet(this, "/api/mobile/categories");
                List<CategoryItem> catList = new Gson().fromJson(catJson, new TypeToken<List<CategoryItem>>(){}.getType());

                String prodJson = ApiClient.httpGet(this, "/api/mobile/products");
                List<ProductItem> prodList = new Gson().fromJson(prodJson, new TypeToken<List<ProductItem>>(){}.getType());

                runOnUiThread(() -> {
                    categories = new ArrayList<>();
                    categories.add(new CategoryItem(0, "Tất cả"));
                    if (catList != null) categories.addAll(catList);

                    allProducts = prodList != null ? prodList : new ArrayList<>();

                    categoryAdapter.setCategories(categories, 0);
                    filterAndRenderProducts();
                });
            } catch (Exception e) {
                e.printStackTrace();
            }
        }).start();
    }

    private void loadExistingCartFromServer() {
        new Thread(() -> {
            try {
                String json = ApiClient.httpGet(this, "/api/mobile/cart/" + currentTableId);
                List<CartItem> serverCart = new Gson().fromJson(json, new TypeToken<List<CartItem>>(){}.getType());
                runOnUiThread(() -> {
                    isLocalModified = false;
                    currentCart.clear();
                    if (serverCart != null) {
                        for (CartItem item : serverCart) {
                            item.isSaved = true;
                            currentCart.add(item);
                        }
                        initialServerCartCount = serverCart.size();
                    } else {
                        initialServerCartCount = 0;
                    }
                    updateCartPanel();
                });
            } catch (Exception e) {
                e.printStackTrace();
            }
        }).start();
    }

    private void loadExistingCartFromServerSilent() {
        new Thread(() -> {
            try {
                String json = ApiClient.httpGet(this, "/api/mobile/cart/" + currentTableId);
                List<CartItem> serverCart = new Gson().fromJson(json, new TypeToken<List<CartItem>>(){}.getType());
                runOnUiThread(() -> applyServerCart(serverCart));
            } catch (Exception ignored) {}
        }).start();
    }

    private void applyServerCart(List<CartItem> serverCart) {
        if (serverCart == null) return;

        // CRUCIAL: Do not disrupt UI if user is actively typing in Note or any input
        View currentFocus = getCurrentFocus();
        if (currentFocus instanceof EditText && currentFocus != edtSearchProduct) {
            return;
        }

        Map<Integer, CartItem> serverMap = new HashMap<>();
        for (CartItem sItem : serverCart) {
            serverMap.put(sItem.id, sItem);
        }

        // 1. Update status/color of existing saved items in place
        boolean statusChanged = false;
        for (CartItem item : currentCart) {
            if (item.id > 0 && serverMap.containsKey(item.id)) {
                CartItem s = serverMap.get(item.id);
                if (s != null && !s.kitchenStatus.equalsIgnoreCase(item.kitchenStatus)) {
                    item.kitchenStatus = s.kitchenStatus;
                    statusChanged = true;
                }
            }
        }

        // 2. Only reload full list if there are NO local modifications or pending changes!
        boolean hasChanges = hasPendingChanges();

        if (!hasChanges) {
            if (serverCart.size() != currentCart.size()) {
                currentCart.clear();
                for (CartItem item : serverCart) {
                    item.isSaved = true;
                    currentCart.add(item);
                }
                initialServerCartCount = serverCart.size();
                updateCartPanel();
            } else if (statusChanged) {
                cartAdapter.notifyDataSetChanged();
            }
        } else if (statusChanged) {
            cartAdapter.notifyDataSetChanged();
        }
    }

    public static String removeVietnameseAccents(String str) {
        if (str == null) return "";
        String nfd = java.text.Normalizer.normalize(str, java.text.Normalizer.Form.NFD);
        java.util.regex.Pattern pattern = java.util.regex.Pattern.compile("\\p{InCombiningDiacriticalMarks}+");
        return pattern.matcher(nfd).replaceAll("").replaceAll("đ", "d").replaceAll("Đ", "D").toLowerCase().trim();
    }

    private void filterAndRenderProducts() {
        List<ProductItem> filtered = new ArrayList<>();
        String cleanSearch = removeVietnameseAccents(currentSearch);
        for (ProductItem p : allProducts) {
            boolean matchCat = (selectedCategoryId == 0 || p.categoryId == selectedCategoryId);
            String cleanName = removeVietnameseAccents(p.name);
            boolean matchSearch = currentSearch.isEmpty() || 
                                  p.name.toLowerCase().contains(currentSearch) ||
                                  cleanName.contains(cleanSearch);
            if (matchCat && matchSearch) {
                filtered.add(p);
            }
        }
        productAdapter.setProducts(filtered);
    }

    @Override
    public void onCategoryClick(CategoryItem category) {
        selectedCategoryId = category.id;
        filterAndRenderProducts();
    }

    @Override
    public void onProductAdd(ProductItem product) {
        isLocalModified = true;

        // Find existing UNSAVED (White) draft item for this product
        CartItem foundDraft = null;
        for (CartItem item : currentCart) {
            if (item.productId == product.id && !item.isSaved) {
                foundDraft = item;
                break;
            }
        }

        if (foundDraft != null) {
            foundDraft.quantity++;
        } else {
            // If all previous entries are saved or finished (Red), create a separate new WHITE card!
            CartItem newItem = new CartItem(product.id, product.name, 1, product.price, "", product.isExcludeFromKitchen);
            newItem.isSaved = false;
            newItem.kitchenStatus = "Draft";
            currentCart.add(newItem);
        }
        updateCartPanel();
    }

    private boolean hasPendingChanges() {
        if (isLocalModified) return true;
        if (currentCart.size() != initialServerCartCount) return true;
        for (CartItem item : currentCart) {
            if (!item.isSaved) return true;
        }
        return false;
    }

    private void updateCartPanel() {
        cartAdapter.setItems(currentCart);

        int totalQty = 0;
        double totalPrice = 0;
        for (CartItem item : currentCart) {
            totalQty += item.quantity;
            totalPrice += item.getTotalPrice();
        }

        tvCartBadge.setText(String.valueOf(totalQty));
        tvCartTotalAmount.setText(fmt.format(totalPrice) + " đ");

        updateSubmitButtonState();
    }

    private void updateSubmitButtonState() {
        // Light up / activate "BÁO BẾP" button ONLY when there are pending changes (White cards or modified items)
        boolean hasChanges = hasPendingChanges();

        if (hasChanges && !currentCart.isEmpty()) {
            btnSubmitOrder.setEnabled(true);
            btnSubmitOrder.setText("🔔 BÁO BẾP");
            btnSubmitOrder.setBackgroundTintList(ColorStateList.valueOf(Color.parseColor("#2563EB")));
            btnSubmitOrder.setTextColor(Color.WHITE);
        } else {
            btnSubmitOrder.setEnabled(false);
            btnSubmitOrder.setText(currentCart.isEmpty() ? "CHƯA CHỌN MÓN" : "✓ ĐÃ BÁO BẾP");
            btnSubmitOrder.setBackgroundTintList(ColorStateList.valueOf(Color.parseColor("#E2E8F0")));
            btnSubmitOrder.setTextColor(Color.parseColor("#94A3B8"));
        }
    }

    @Override
    public void onQuantityChanged(CartItem item, int newQty) {
        isLocalModified = true;
        if (newQty <= 0) {
            currentCart.remove(item);
        } else {
            item.quantity = newQty;
            item.isSaved = false; // Turn from Green to WHITE (modified/pending update)
        }
        updateCartPanel();
    }

    @Override
    public void onAddNewRound(CartItem item) {
        isLocalModified = true;

        // Find existing UNSAVED (White) draft item for this product
        CartItem foundDraft = null;
        for (CartItem ci : currentCart) {
            if (ci.productId == item.productId && !ci.isSaved) {
                foundDraft = ci;
                break;
            }
        }

        if (foundDraft != null) {
            foundDraft.quantity++;
        } else {
            // Add a new separate WHITE card (New batch / round)
            CartItem newItem = new CartItem(item.productId, item.productName, 1, item.price, "", item.isExcludeFromKitchen);
            newItem.isSaved = false;
            newItem.kitchenStatus = "Draft";
            currentCart.add(newItem);
        }
        updateCartPanel();
    }

    @Override
    public void onNoteChanged(CartItem item, String newNote) {
        isLocalModified = true;
        item.note = newNote;
        item.isSaved = false; // Mark modified
        // Crucial: Update button state without calling notifyDataSetChanged to prevent stealing focus!
        updateSubmitButtonState();
    }

    @Override
    public void onItemDeleted(CartItem item) {
        isLocalModified = true;
        currentCart.remove(item);
        updateCartPanel();
    }

    private void submitOrderToKitchen() {
        if (currentCart.isEmpty() && initialServerCartCount == 0) {
            Toast.makeText(this, "Chưa có món nào để báo bếp!", Toast.LENGTH_SHORT).show();
            return;
        }

        btnSubmitOrder.setEnabled(false);
        btnSubmitOrder.setText("ĐANG GỬI BÁO BẾP...");

        OrderRequest req = new OrderRequest();
        req.tableId = currentTableId;
        for (CartItem item : currentCart) {
            req.items.add(new OrderRequest.OrderItem(item.id, item.productId, item.quantity, item.note));
        }

        new Thread(() -> {
            try {
                ApiClient.httpPostJson(this, "/api/mobile/order", req);
                runOnUiThread(() -> {
                    Toast.makeText(MenuActivity.this, "Đã báo bếp thành công!", Toast.LENGTH_SHORT).show();
                    isLocalModified = false;
                    for (CartItem item : currentCart) {
                        item.isSaved = true;
                    }
                    initialServerCartCount = currentCart.size();
                    updateCartPanel();
                    loadExistingCartFromServer();
                });
            } catch (Exception e) {
                runOnUiThread(() -> {
                    updateCartPanel();
                    Toast.makeText(MenuActivity.this, "Lỗi gửi báo bếp: " + e.getMessage(), Toast.LENGTH_LONG).show();
                });
            }
        }).start();
    }
}
