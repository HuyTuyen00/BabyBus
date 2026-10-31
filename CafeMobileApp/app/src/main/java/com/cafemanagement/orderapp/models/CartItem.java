package com.cafemanagement.orderapp.models;

public class CartItem {
    public int id;
    public int productId;
    public String productName;
    public int quantity;
    public double price;
    public String note = "";
    public String kitchenStatus = "Pending";
    public boolean isExcludeFromKitchen = false;
    public boolean isSaved = false;

    public CartItem(int productId, String productName, int quantity, double price, String note) {
        this.productId = productId;
        this.productName = productName;
        this.quantity = quantity;
        this.price = price;
        this.note = note != null ? note : "";
        this.isSaved = false;
        this.kitchenStatus = "Draft";
    }

    public CartItem(int productId, String productName, int quantity, double price, String note, boolean isExcludeFromKitchen) {
        this.productId = productId;
        this.productName = productName;
        this.quantity = quantity;
        this.price = price;
        this.note = note != null ? note : "";
        this.isExcludeFromKitchen = isExcludeFromKitchen;
        this.isSaved = false;
        this.kitchenStatus = "Draft";
    }

    public boolean isCompleted() {
        return isSaved && ("Completed".equalsIgnoreCase(kitchenStatus) || "Served".equalsIgnoreCase(kitchenStatus));
    }

    public double getTotalPrice() {
        return price * quantity;
    }
}
