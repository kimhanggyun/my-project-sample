package net.itgold.unitygame1.auth;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.NoCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.util.concurrent.Executor;
import java.util.concurrent.Executors;

/**
 * 구글 로그인 (Android Credential Manager): 고른 구글 계정의 ID 토큰을 돌려준다.
 * Unity(C#)는 AndroidJavaProxy로 Callback을 구현해 받는다. 결과는 별도 스레드에서 오므로 C# 쪽에서 메인 스레드로 넘긴다.
 */
public final class GoogleSignInBridge {
    public interface Callback {
        /** 성공이면 idToken, 실패면 error (noCredential = 이 기기에 고를 계정 없음 / 이전에 승인한 계정 없음). */
        void onResult(String idToken, String error);
    }

    private static final Executor EXECUTOR = Executors.newSingleThreadExecutor();

    private GoogleSignInBridge() { }

    /**
     * @param webClientId Firebase의 웹 클라이언트 ID (google-services.json의 client_type 3)
     * @param onlyAuthorized true면 이전에 이 앱에 승인한 계정만 (자동 로그인), false면 계정 고르기 창
     */
    public static void signIn(final Activity activity, String webClientId, boolean onlyAuthorized, final Callback callback) {
        try {
            GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                    .setFilterByAuthorizedAccounts(onlyAuthorized)
                    .setServerClientId(webClientId)
                    .setAutoSelectEnabled(onlyAuthorized)
                    .build();
            GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
            CredentialManager manager = CredentialManager.create(activity);
            manager.getCredentialAsync(activity, request, new CancellationSignal(), EXECUTOR,
                    new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                        @Override
                        public void onResult(GetCredentialResponse response) {
                            Credential c = response.getCredential();
                            if (c instanceof CustomCredential && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(c.getType())) {
                                try {
                                    callback.onResult(GoogleIdTokenCredential.createFrom(((CustomCredential) c).getData()).getIdToken(), null);
                                } catch (Exception e) {
                                    callback.onResult(null, "badCredential: " + e);
                                }
                            } else {
                                callback.onResult(null, "unexpectedCredential: " + c.getType());
                            }
                        }

                        @Override
                        public void onError(GetCredentialException e) {
                            callback.onResult(null, (e instanceof NoCredentialException ? "noCredential: " : e.getClass().getSimpleName() + ": ") + e.getMessage());
                        }
                    });
        } catch (Exception e) {
            callback.onResult(null, "start: " + e);
        }
    }
}
