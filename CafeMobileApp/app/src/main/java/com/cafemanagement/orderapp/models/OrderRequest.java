package com.cafemanagement.orderapp.models;

import java.util.ArrayList;
import java.util.List;

public class OrderRequest {
    public int tableId;
    public List<OrderItem> items = new ArrayList<>();

    public static class OrderItem {
        public int id;
        public int productId;
        public int quantity;
        public String note = "";

        public OrderItem(int productId, int quantity, String note) {
            this.id = 0;
            this.productId = productId;
            this.quantity = quantity;
            this.note = note != null ? note : "";
        }

        public OrderItem(int id, int productId, int quantity, String note) {
            this.id = id;
            this.productId = productId;
            this.quantity = quantity;
            this.note = note != null ? note : "";
        }
    }
}
