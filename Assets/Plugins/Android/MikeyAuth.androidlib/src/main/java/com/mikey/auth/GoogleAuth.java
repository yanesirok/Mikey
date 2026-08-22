package com.mikey.auth;

import android.app.Activity;
import android.os.CancellationSignal;
import android.util.Log;

import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.security.MessageDigest;
import java.security.SecureRandom;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Вход через Google: Credential Manager показывает системный диалог и отдаёт
 * ID-токен, который Unity меняет в Supabase на сессию.
 *
 * Результат забирается опросом (consumeIdToken), а не колбэком в Unity: диалог
 * асинхронный, а этот приём в проекте уже используется — MikeyPose.androidlib
 * отдаёт кадры через readLatest. Второй механизм ради одного вызова заводить
 * незачем.
 */
public final class GoogleAuth {

    private static final String TAG = "MikeyAuth";

    private final Activity activity;
    private final CredentialManager credentialManager;
    private final ExecutorService callbackExecutor = Executors.newSingleThreadExecutor();

    private volatile String idToken;
    private volatile String error;
    private volatile String rawNonce;

    public GoogleAuth(Activity activity) {
        this.activity = activity;
        this.credentialManager = CredentialManager.create(activity);
    }

    /** Запускает диалог входа. Результат забирать через consumeIdToken/consumeError. */
    public void requestIdToken(String webClientId) {
        idToken = null;
        error = null;

        // Сырое значение уходит в Unity и дальше в Supabase; в сам токен Google
        // кладёт его SHA-256. Supabase хеширует присланное сырое и сверяет с тем,
        // что в токене — так перехваченный чужой токен не подойдёт.
        byte[] raw = new byte[32];
        new SecureRandom().nextBytes(raw);
        StringBuilder rawHex = new StringBuilder(raw.length * 2);
        for (byte b : raw) rawHex.append(String.format("%02x", b));
        rawNonce = rawHex.toString();

        String hashedNonce;
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256")
                    .digest(rawNonce.getBytes(StandardCharsets.UTF_8));
            StringBuilder hashHex = new StringBuilder(digest.length * 2);
            for (byte b : digest) hashHex.append(String.format("%02x", b));
            hashedNonce = hashHex.toString();
        } catch (Exception e) {
            Log.e(TAG, "не удалось посчитать nonce", e);
            error = String.valueOf(e.getMessage());
            return;
        }

        GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                .setServerClientId(webClientId)
                .setFilterByAuthorizedAccounts(false)
                .setNonce(hashedNonce)
                .build();

        GetCredentialRequest request = new GetCredentialRequest.Builder()
                .addCredentialOption(option)
                .build();

        credentialManager.getCredentialAsync(
                activity,
                request,
                new CancellationSignal(),
                callbackExecutor,
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override
                    public void onResult(GetCredentialResponse response) {
                        try {
                            GoogleIdTokenCredential credential =
                                    GoogleIdTokenCredential.createFrom(
                                            response.getCredential().getData());
                            idToken = credential.getIdToken();
                        } catch (Exception e) {
                            Log.e(TAG, "не удалось разобрать учётные данные", e);
                            error = String.valueOf(e.getMessage());
                        }
                    }

                    @Override
                    public void onError(GetCredentialException e) {
                        Log.e(TAG, "вход отменён или не удался", e);
                        error = String.valueOf(e.getMessage());
                    }
                });
    }

    /** ID-токен, либо null, если вход ещё идёт. Отдаётся один раз. */
    public String consumeIdToken() {
        String value = idToken;
        idToken = null;
        return value;
    }

    /** Текст ошибки, либо null. Отдаётся один раз. */
    public String consumeError() {
        String value = error;
        error = null;
        return value;
    }

    /** Сырой nonce той же попытки входа. Отдаётся один раз, вместе с токеном. */
    public String consumeNonce() {
        String value = rawNonce;
        rawNonce = null;
        return value;
    }
}
