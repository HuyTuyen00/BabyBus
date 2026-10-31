package com.cafemanagement.orderapp.api;

import android.content.Context;
import android.content.SharedPreferences;

import com.google.gson.Gson;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;

public class ApiClient {

    private static final String PREF_NAME = "CafeConfig";
    private static final String KEY_SERVER_IP = "server_ip";
    private static final String DEFAULT_IP = "192.168.1.15:5000";

    public static String getServerIp(Context context) {
        SharedPreferences sp = context.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE);
        return sp.getString(KEY_SERVER_IP, DEFAULT_IP);
    }

    public static void setServerIp(Context context, String ip) {
        SharedPreferences sp = context.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE);
        sp.edit().putString(KEY_SERVER_IP, ip.trim()).apply();
    }

    public static String getBaseUrl(Context context) {
        String ip = getServerIp(context);
        if (!ip.startsWith("http://") && !ip.startsWith("https://")) {
            ip = "http://" + ip;
        }
        return ip;
    }

    public static String httpGet(Context context, String endpoint) throws Exception {
        String fullUrl = getBaseUrl(context) + endpoint;
        URL url = new URL(fullUrl);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        conn.setRequestMethod("GET");
        conn.setConnectTimeout(5000);
        conn.setReadTimeout(5000);

        int code = conn.getResponseCode();
        if (code == 200) {
            BufferedReader br = new BufferedReader(new InputStreamReader(conn.getInputStream(), "UTF-8"));
            StringBuilder sb = new StringBuilder();
            String line;
            while ((line = br.readLine()) != null) {
                sb.append(line);
            }
            br.close();
            return sb.toString();
        } else {
            throw new Exception("HTTP Error: " + code);
        }
    }

    public static String httpPostJson(Context context, String endpoint, Object jsonBodyObj) throws Exception {
        String fullUrl = getBaseUrl(context) + endpoint;
        URL url = new URL(fullUrl);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        conn.setRequestMethod("POST");
        conn.setDoOutput(true);
        conn.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
        conn.setConnectTimeout(5000);
        conn.setReadTimeout(5000);

        String jsonStr = new Gson().toJson(jsonBodyObj);
        byte[] input = jsonStr.getBytes("UTF-8");

        try (OutputStream os = conn.getOutputStream()) {
            os.write(input, 0, input.length);
        }

        int code = conn.getResponseCode();
        if (code == 200) {
            BufferedReader br = new BufferedReader(new InputStreamReader(conn.getInputStream(), "UTF-8"));
            StringBuilder sb = new StringBuilder();
            String line;
            while ((line = br.readLine()) != null) {
                sb.append(line);
            }
            br.close();
            return sb.toString();
        } else {
            throw new Exception("HTTP Error: " + code);
        }
    }
}
