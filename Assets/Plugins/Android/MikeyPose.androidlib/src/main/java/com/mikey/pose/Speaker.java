package com.mikey.pose;

import android.app.Activity;
import android.speech.tts.TextToSpeech;

import java.util.Locale;

/**
 * Thin wrapper over Android TextToSpeech for spoken coaching cues, driven from Unity
 * (see AndroidVoice.cs). Prefers a Russian voice, falling back to the device default.
 */
public class Speaker {

    private TextToSpeech tts;
    private volatile boolean ready = false;

    public Speaker(Activity activity) {
        tts = new TextToSpeech(activity.getApplicationContext(), new TextToSpeech.OnInitListener() {
            @Override public void onInit(int status) {
                if (status == TextToSpeech.SUCCESS && tts != null) {
                    int r = tts.setLanguage(new Locale("ru"));
                    if (r == TextToSpeech.LANG_MISSING_DATA || r == TextToSpeech.LANG_NOT_SUPPORTED) {
                        tts.setLanguage(Locale.getDefault());
                    }
                    ready = true;
                }
            }
        });
    }

    /** Speaks the text immediately, interrupting any current utterance. */
    public void speak(String text) {
        if (!ready || tts == null || text == null || text.isEmpty()) {
            return;
        }
        tts.speak(text, TextToSpeech.QUEUE_FLUSH, null, "cue");
    }

    public void stop() {
        if (tts != null) {
            tts.stop();
            tts.shutdown();
            tts = null;
        }
        ready = false;
    }
}
