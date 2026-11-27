package com.loopstudio.taa.login;

import android.content.Context;
import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;

/**
 * 마지막으로 로그인에 성공한 방식(예: "GPGS2")을 저장한다.
 * 안드로이드 자동 백업에서 제외되는 폴더(no_backup)에 저장하므로
 * 앱 데이터를 삭제하거나 앱을 지우고 다시 설치하면 사라진다. → 다시 로그인 방식 선택 화면부터 시작
 */
public final class TaaLoginStore {
    private static final String TAG = "TaaLogin";
    private static final String FILE_NAME = "taa_last_login_type";

    private TaaLoginStore() {}

    /** 저장된 로그인 방식. 없으면 빈 문자열 */
    public static String get(Context context) {
        try {
            File file = new File(context.getNoBackupFilesDir(), FILE_NAME);
            if (!file.exists()) return "";

            byte[] data = new byte[(int) file.length()];
            int read = 0;
            try (FileInputStream in = new FileInputStream(file)) {
                while (read < data.length) {
                    int n = in.read(data, read, data.length - read);
                    if (n < 0) break;
                    read += n;
                }
            }
            return new String(data, 0, read, StandardCharsets.UTF_8).trim();
        } catch (Exception e) {
            Log.w(TAG, "로그인 방식 읽기 실패", e);
            return "";
        }
    }

    /** 로그인 방식 저장. null 이나 빈 문자열이면 삭제(로그아웃) */
    public static void set(Context context, String type) {
        try {
            File file = new File(context.getNoBackupFilesDir(), FILE_NAME);
            if (type == null || type.isEmpty()) {
                if (file.exists() && !file.delete()) Log.w(TAG, "로그인 방식 삭제 실패");
                return;
            }
            try (FileOutputStream out = new FileOutputStream(file, false)) {
                out.write(type.getBytes(StandardCharsets.UTF_8));
            }
        } catch (Exception e) {
            Log.w(TAG, "로그인 방식 저장 실패", e);
        }
    }
}
