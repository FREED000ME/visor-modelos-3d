package com.freed.armado;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.ClipData;
import android.content.ContentValues;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.MediaStore;
import android.speech.tts.TextToSpeech;
import android.util.Base64;
import android.view.View;
import android.webkit.JavascriptInterface;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import androidx.core.content.FileProvider;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends Activity {
    private static final int REQ_FILES = 41;
    private WebView web;
    private ValueCallback<Uri[]> pendingFiles;
    private Uri cameraUri;
    private TextToSpeech tts;
    private boolean ttsReady = false;
    private final ExecutorService pool = Executors.newFixedThreadPool(2);
    private final Handler main = new Handler(Looper.getMainLooper());

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        web = new WebView(this);
        setContentView(web);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setAllowFileAccess(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setTextZoom(100);
        web.setWebViewClient(new WebViewClient());
        web.setWebChromeClient(new Chrome());
        web.addJavascriptInterface(new Bridge(), "Android");
        if (savedInstanceState != null) web.restoreState(savedInstanceState);
        else web.loadUrl("file:///android_asset/index.html");

        tts = new TextToSpeech(this, status -> {
            if (status == TextToSpeech.SUCCESS) {
                int r = tts.setLanguage(new Locale("es", "ES"));
                if (r < 0) tts.setLanguage(new Locale("es"));
                ttsReady = true;
            }
        });
    }

    @Override
    protected void onSaveInstanceState(Bundle out) {
        super.onSaveInstanceState(out);
        web.saveState(out);
    }

    @Override
    public void onBackPressed() {
        web.evaluateJavascript("window.__back ? window.__back() : false", v -> {
            if (!"true".equals(v)) MainActivity.super.onBackPressed();
        });
    }

    @Override
    protected void onDestroy() {
        if (tts != null) tts.shutdown();
        pool.shutdownNow();
        super.onDestroy();
    }

    /* ---------- file chooser: camera or gallery ---------- */
    private class Chrome extends WebChromeClient {
        @Override
        public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
            if (pendingFiles != null) pendingFiles.onReceiveValue(null);
            pendingFiles = callback;
            cameraUri = null;
            try {
                if (params.isCaptureEnabled()) {
                    File f = new File(getCacheDir(), "foto_" + System.currentTimeMillis() + ".jpg");
                    cameraUri = FileProvider.getUriForFile(MainActivity.this, "com.freed.armado.files", f);
                    Intent cam = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
                    cam.putExtra(MediaStore.EXTRA_OUTPUT, cameraUri);
                    cam.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivityForResult(cam, REQ_FILES);
                } else {
                    Intent pick = new Intent(Intent.ACTION_GET_CONTENT);
                    pick.addCategory(Intent.CATEGORY_OPENABLE);
                    String[] types = params.getAcceptTypes();
                    String type = (types != null && types.length > 0 && types[0] != null && !types[0].isEmpty()) ? types[0] : "*/*";
                    if (type.contains(",")) type = type.split(",")[0];
                    if (type.startsWith(".")) type = "*/*";
                    if (type.contains("json")) type = "*/*";
                    pick.setType(type);
                    if (params.getMode() == FileChooserParams.MODE_OPEN_MULTIPLE)
                        pick.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                    startActivityForResult(Intent.createChooser(pick, "Elegir archivo"), REQ_FILES);
                }
            } catch (Exception e) {
                pendingFiles = null;
                toast("No se pudo abrir: " + e.getMessage());
                return false;
            }
            return true;
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_FILES || pendingFiles == null) return;
        Uri[] result = null;
        if (resultCode == RESULT_OK) {
            if (cameraUri != null) {
                result = new Uri[]{cameraUri};
            } else if (data != null) {
                ClipData clip = data.getClipData();
                if (clip != null && clip.getItemCount() > 0) {
                    ArrayList<Uri> list = new ArrayList<>();
                    for (int i = 0; i < clip.getItemCount(); i++) list.add(clip.getItemAt(i).getUri());
                    result = list.toArray(new Uri[0]);
                } else if (data.getData() != null) {
                    result = new Uri[]{data.getData()};
                }
            }
        }
        pendingFiles.onReceiveValue(result);
        pendingFiles = null;
        cameraUri = null;
    }

    private void toast(String m) {
        main.post(() -> Toast.makeText(this, m, Toast.LENGTH_LONG).show());
    }

    private void js(String code) {
        main.post(() -> web.evaluateJavascript(code, null));
    }

    /* ---------- bridge used by index.html ---------- */
    private class Bridge {
        @JavascriptInterface
        public void post(String url, String headersJson, String body, String id) {
            pool.execute(() -> {
                int status = 0;
                String text;
                HttpURLConnection c = null;
                try {
                    c = (HttpURLConnection) new URL(url).openConnection();
                    c.setRequestMethod("POST");
                    c.setConnectTimeout(30000);
                    c.setReadTimeout(600000);
                    c.setDoOutput(true);
                    JSONObject h = new JSONObject(headersJson);
                    Iterator<String> keys = h.keys();
                    while (keys.hasNext()) { String k = keys.next(); c.setRequestProperty(k, h.getString(k)); }
                    try (OutputStream os = c.getOutputStream()) { os.write(body.getBytes(StandardCharsets.UTF_8)); }
                    status = c.getResponseCode();
                    InputStream in = status >= 400 ? c.getErrorStream() : c.getInputStream();
                    text = in == null ? "" : readAll(in);
                } catch (Exception e) {
                    text = "{\"error\":{\"message\":" + JSONObject.quote(String.valueOf(e.getMessage())) + "}}";
                } finally {
                    if (c != null) c.disconnect();
                }
                js("window.__nativeCb(" + JSONObject.quote(id) + "," + status + "," + JSONObject.quote(text) + ")");
            });
        }

        @JavascriptInterface
        public void saveFile(String name, String b64, String id) {
            pool.execute(() -> {
                try {
                    byte[] bytes = Base64.decode(b64, Base64.DEFAULT);
                    String mime = mimeOf(name);
                    if (Build.VERSION.SDK_INT >= 29) {
                        ContentValues v = new ContentValues();
                        v.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                        v.put(MediaStore.MediaColumns.MIME_TYPE, mime);
                        v.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/ArmadoPasoAPaso");
                        Uri uri = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
                        if (uri == null) throw new Exception("sin acceso a Descargas");
                        try (OutputStream os = getContentResolver().openOutputStream(uri)) { os.write(bytes); }
                        js("window.__nativeSaved(" + JSONObject.quote(id) + ",true," + JSONObject.quote("Guardado en Descargas/ArmadoPasoAPaso") + ")");
                    } else {
                        File dir = getExternalFilesDir(null);
                        File f = new File(dir, name);
                        try (FileOutputStream os = new FileOutputStream(f)) { os.write(bytes); }
                        Uri uri = FileProvider.getUriForFile(MainActivity.this, "com.freed.armado.files", f);
                        Intent share = new Intent(Intent.ACTION_SEND);
                        share.setType(mime);
                        share.putExtra(Intent.EXTRA_STREAM, uri);
                        share.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                        main.post(() -> startActivity(Intent.createChooser(share, "Guardar o compartir")));
                        js("window.__nativeSaved(" + JSONObject.quote(id) + ",true," + JSONObject.quote("Listo para compartir") + ")");
                    }
                } catch (Exception e) {
                    js("window.__nativeSaved(" + JSONObject.quote(id) + ",false," + JSONObject.quote(String.valueOf(e.getMessage())) + ")");
                }
            });
        }

        @JavascriptInterface
        public void speak(String text) {
            if (ttsReady) tts.speak(text, TextToSpeech.QUEUE_FLUSH, null, "paso");
        }

        @JavascriptInterface
        public void stopSpeak() {
            if (ttsReady) tts.stop();
        }
    }

    private static String mimeOf(String name) {
        String n = name.toLowerCase(Locale.ROOT);
        if (n.endsWith(".mp4")) return "video/mp4";
        if (n.endsWith(".webm")) return "video/webm";
        if (n.endsWith(".json")) return "application/json";
        if (n.endsWith(".png")) return "image/png";
        return "application/octet-stream";
    }

    private static String readAll(InputStream in) throws Exception {
        ByteArrayOutputStream bo = new ByteArrayOutputStream();
        byte[] buf = new byte[16384];
        int r;
        while ((r = in.read(buf)) > 0) bo.write(buf, 0, r);
        return bo.toString("UTF-8");
    }
}
