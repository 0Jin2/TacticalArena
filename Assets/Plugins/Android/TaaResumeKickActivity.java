package com.loopstudio.taa.login;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

/**
 * 화면 없이 열렸다가 바로 닫히는 투명 화면.
 * 게임 화면이 "잠깐 멈췄다가 다시 켜진 것"처럼 만들어서,
 * 늦게 시작한 Play 게임즈가 현재 게임 화면을 인식하게 한다.
 *  → 구글 버튼을 눌러 Play 게임즈를 처음 시작했을 때도 환영 배너 / 로그인 창이 정상 표시
 */
public final class TaaResumeKickActivity extends Activity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        finish();
        overridePendingTransition(0, 0);
    }

    /** Unity 에서 호출: 투명 화면을 한 번 열었다 닫는다 */
    public static void kick(final Activity activity) {
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                Intent intent = new Intent(activity, TaaResumeKickActivity.class);
                intent.addFlags(Intent.FLAG_ACTIVITY_NO_ANIMATION);
                activity.startActivity(intent);
                activity.overridePendingTransition(0, 0);
            }
        });
    }
}
