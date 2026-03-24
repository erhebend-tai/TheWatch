package com.thewatch.app

import android.os.Bundle
import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.navigation.compose.rememberNavController
import com.thewatch.app.navigation.NavGraph
import com.thewatch.app.service.QuickTapDetector
import com.thewatch.app.ui.theme.TheWatchTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    @Inject
    lateinit var quickTapDetector: QuickTapDetector

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            TheWatchTheme {
                Surface(
                    modifier = Modifier.fillMaxSize()
                ) {
                    val navController = rememberNavController()
                    NavGraph(navController = navController)
                }
            }
        }
    }

    /**
     * Intercept key events for quick-tap SOS detection.
     * Volume button presses are forwarded to the QuickTapDetector.
     * If detection is enabled and the event is consumed, volume won't change.
     */
    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (quickTapDetector.onKeyEvent(event)) {
            return true // Consumed — don't let volume change
        }
        return super.dispatchKeyEvent(event)
    }
}
