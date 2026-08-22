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

import java.security.SecureRandom;
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

    public GoogleAuth(Activity activity) {
        this.activity = activity;
        this.credentialManager = CredentialManager.create(activity);
    }

    /** Запускает диалог входа. Результат забирать через consumeIdToken/consumeError. */
    public void requestIdToken(String webClientId) {
        idToken = null;
        error = null;

        // nonce привязывает выданный токен к этой попытке входа: перехваченный
        // чужой токен с другим nonce Supabase не примет.
        byte[] raw = new byte[32];
        new SecureRandom().nextBytes(raw);
        StringBuilder nonce = new StringBuilder(raw.length * 2);
        for (byte b : raw) nonce.append(String.format("%02x", b));

        GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                .setServerClientId(webClientId)
                .setFilterByAuthorizedAccounts(false)
                .setNonce(nonce.toString())
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
}
