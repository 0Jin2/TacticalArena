package com.loopstudio.taa.login;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.util.Log;

import com.google.android.gms.games.PlayGamesSdk;

/**
 * 앱이 켜질 때 가장 먼저 실행되는 부품.
 * 지난번에 구글(GPGS2)로 로그인했던 기기에서만 Play 게임즈를 바로 시작한다.
 *  → 정상적인 시점에 시작되므로 자동 로그인 + "Play 게임즈 ○○님 환영합니다" 배너가 정상 동작
 * 처음 설치한 기기(로그인 기록 없음)에서는 아무것도 하지 않는다. → 첫 실행에 구글 배너가 뜨지 않음
 *
 * (원래 Play 게임즈 라이브러리의 PlayGamesInitProvider 는 무조건 시작하기 때문에 매니페스트에서 제거했다)
 */
public final class TaaPlayGamesInitProvider extends ContentProvider {
    private static final String TAG = "TaaLogin";
    private static final String LOGIN_TYPE_GOOGLE = "GPGS2";

    @Override
    public boolean onCreate() {
        Context context = getContext();
        if (context != null && LOGIN_TYPE_GOOGLE.equals(TaaLoginStore.get(context))) {
            try {
                PlayGamesSdk.initialize(context);
                Log.d(TAG, "지난번 구글 로그인 기록 있음 → Play 게임즈 시작");
            } catch (Throwable t) {
                Log.w(TAG, "Play 게임즈 시작 실패", t);
            }
        }
        return true;
    }

    // 아래는 ContentProvider 필수 함수 (사용하지 않음)
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) { return null; }
    @Override public String getType(Uri uri) { return null; }
    @Override public Uri insert(Uri uri, ContentValues values) { return null; }
    @Override public int delete(Uri uri, String selection, String[] selectionArgs) { return 0; }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) { return 0; }
}
