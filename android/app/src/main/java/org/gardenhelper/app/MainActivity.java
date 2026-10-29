package org.gardenhelper.app;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.Menu;
import android.view.MenuItem;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * Garden Helper phone app: a thin full-screen shell around the web page that the desktop app serves.
 * On first start it asks for the address of the PC (or the phone link shown on the Connect page), then remembers it.
 * The secret token lives in the page's own storage; this shell never reads it.
 */
public class MainActivity extends Activity {
    private static final String PREFS = "garden_helper";
    private static final String KEY_SERVER = "server";

    private WebView web;
    private String server;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        server = getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY_SERVER, null);
        if (server == null) showSetup(null); else showWeb(null);
    }

    private int dp(int v) { return (int) (v * getResources().getDisplayMetrics().density); }

    private TextView text(String s, int sp, boolean bold) {
        TextView t = new TextView(this);
        t.setText(s); t.setTextSize(sp); t.setTextColor(Color.parseColor("#1B2418"));
        if (bold) t.setTypeface(t.getTypeface(), android.graphics.Typeface.BOLD);
        return t;
    }

    /** Address entry screen. {@code problem} is shown above the field when the last attempt failed. */
    private void showSetup(String problem) {
        setTitle(getString(R.string.app_name));
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(24), dp(32), dp(24), dp(24));
        box.setBackgroundColor(Color.parseColor("#F4F1E8"));

        box.addView(text(getString(R.string.setup_title), 22, true));
        TextView help = text(getString(R.string.setup_help), 15, false);
        help.setPadding(0, dp(12), 0, dp(12));
        box.addView(help);
        if (problem != null) {
            TextView p = text(problem, 15, true);
            p.setTextColor(Color.parseColor("#B3341F"));
            p.setPadding(0, 0, 0, dp(8));
            box.addView(p);
        }
        final EditText field = new EditText(this);
        field.setHint("192.168.1.20:8765");
        field.setSingleLine(true);
        field.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        field.setContentDescription(getString(R.string.setup_title));
        if (server != null) field.setText(server);
        box.addView(field, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        Button go = new Button(this);
        go.setText(R.string.connect);
        go.setMinimumHeight(dp(52));
        go.setOnClickListener(v -> {
            String url = normalise(field.getText().toString());
            if (url == null) { showSetup(getString(R.string.bad_address)); return; }
            server = url.split("#")[0];
            getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY_SERVER, server).apply();
            showWeb(url);     // a pasted phone link keeps its #t=token part so the page can save the token
        });
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(16);
        box.addView(go, lp);

        ScrollView sv = new ScrollView(this);
        sv.setFillViewport(true);
        sv.addView(box);
        setContentView(sv);
        web = null;
    }

    /** Turns "192.168.1.20:8765", "http://host:8765/" or a full phone link into a clean http(s) URL; null when it is not usable. */
    static String normalise(String raw) {
        String s = raw == null ? "" : raw.trim();
        if (s.isEmpty() || s.contains(" ")) return null;
        if (!s.startsWith("http://") && !s.startsWith("https://")) s = "http://" + s;
        Uri u = Uri.parse(s);
        if (u.getHost() == null || u.getHost().isEmpty()) return null;
        String path = u.getPath() == null || u.getPath().equals("/") ? "" : u.getPath();
        String frag = u.getEncodedFragment();
        return u.getScheme() + "://" + u.getEncodedAuthority() + path + (frag != null ? "#" + frag : "");
    }

    private void showWeb(String url) {
        web = new WebView(this);
        WebSettings ws = web.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setAllowFileAccess(false);
        ws.setAllowContentAccess(false);
        ws.setMediaPlaybackRequiresUserGesture(true);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest r) {
                Uri target = r.getUrl();
                Uri home = Uri.parse(server);
                if (target.getHost() != null && target.getHost().equals(home.getHost()) && target.getPort() == home.getPort()) return false;
                startActivity(new Intent(Intent.ACTION_VIEW, target));      // anything else opens in the browser, never inside this app
                return true;
            }
            @Override public void onReceivedError(WebView v, WebResourceRequest r, WebResourceError e) {
                if (r.isForMainFrame()) showSetup(getString(R.string.cannot_reach) + " " + server);
            }
        });
        setContentView(web);
        web.loadUrl(url != null ? url : server + "/");
    }

    @Override public boolean onCreateOptionsMenu(Menu menu) {
        menu.add(0, 1, 0, R.string.change_server).setShowAsAction(MenuItem.SHOW_AS_ACTION_NEVER);
        menu.add(0, 2, 1, R.string.reload).setShowAsAction(MenuItem.SHOW_AS_ACTION_NEVER);
        return true;
    }

    @Override public boolean onOptionsItemSelected(MenuItem item) {
        if (item.getItemId() == 1) { showSetup(null); return true; }
        if (item.getItemId() == 2 && web != null) { web.reload(); return true; }
        return super.onOptionsItemSelected(item);
    }

    @Override public void onBackPressed() {
        if (web != null && web.canGoBack()) web.goBack(); else super.onBackPressed();
    }
}
