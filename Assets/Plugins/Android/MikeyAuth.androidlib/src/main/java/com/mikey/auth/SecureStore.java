package com.mikey.auth;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import androidx.security.crypto.EncryptedSharedPreferences;
import androidx.security.crypto.MasterKey;

/**
 * Шифрованное хранилище для refresh-токена. Не PlayerPrefs: там это обычный
 * XML, который вдобавок попадает в автоматический бэкап Android, то есть
 * долгоживущий ключ от аккаунта уезжал бы в облако открытым текстом.
 */
public final class SecureStore {

    private static final String TAG = "MikeyAuth";
    private static final String FILE = "mikey_auth";

    private final SharedPreferences prefs;

    public SecureStore(Context context) throws Exception {
        MasterKey key = new MasterKey.Builder(context)
                .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
                .build();

        prefs = EncryptedSharedPreferences.create(
                context,
                FILE,
                key,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM);
    }

    public void put(String key, String value) {
        prefs.edit().putString(key, value).apply();
    }

    /** Значение или null. */
    public String get(String key) {
        try {
            return prefs.getString(key, null);
        } catch (Exception e) {
            // Испорченное хранилище лечится повторным входом, а не падением.
            Log.w(TAG, "не удалось прочитать значение, считаем отсутствующим", e);
            return null;
        }
    }

    public void remove(String key) {
        prefs.edit().remove(key).apply();
    }
}
