package com.thewatch.app.ui.screens.emailverify

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.NavController
import com.thewatch.app.navigation.NavRoute
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

/**
 * Email verification prompt screen, shown after signup if the user's email
 * has not been verified yet.
 *
 * Features:
 *   - "Resend Verification Email" button with 30-second cooldown
 *   - "I've Verified My Email" button that refreshes the Firebase token
 *     and checks if emailVerified is now true
 *   - Auto-check on lifecycle resume: when the user clicks the link in
 *     their email and returns to the app, we automatically re-check
 *
 * Write-Ahead Log:
 *   - WAL Entry: EMAIL_VERIFY_SCREEN_RENDER -> timestamp
 *   - WAL Entry: EMAIL_VERIFY_RESEND_TAP    -> cooldownRemaining, timestamp
 *   - WAL Entry: EMAIL_VERIFY_CHECK_TAP     -> timestamp
 *   - WAL Entry: EMAIL_VERIFY_AUTO_CHECK    -> lifecycle=ON_RESUME, timestamp
 *   - WAL Entry: EMAIL_VERIFY_NAV_APP       -> verified=true, timestamp
 *   - WAL Entry: EMAIL_VERIFY_NAV_LOGIN     -> userAction=back, timestamp
 *
 * Example navigation setup:
 *   composable(NavRoute.EmailVerify.route) {
 *       EmailVerifyScreen(navController = navController)
 *   }
 */
@Composable
fun EmailVerifyScreen(
    navController: NavController,
    viewModel: EmailVerifyViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    val resendCooldown by viewModel.resendCooldown.collectAsState()
    val lifecycleOwner = LocalLifecycleOwner.current

    // Auto-check verification status when the user returns from their email app
    LaunchedEffect(lifecycleOwner) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            viewModel.checkVerificationStatus()
        }
    }

    // Navigate to app on successful verification
    LaunchedEffect(uiState) {
        if (uiState is EmailVerifyUiState.Verified) {
            navController.navigate(NavRoute.AppGraph.route) {
                popUpTo(NavRoute.AuthGraph.route) { inclusive = true }
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(White)
            .verticalScroll(rememberScrollState()),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // ── Mail Icon ───────────────────────────────────────────
            Box(
                modifier = Modifier
                    .size(80.dp)
                    .background(color = Color(0xFFE3F2FD), shape = RoundedCornerShape(16.dp)),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "@",
                    fontSize = 36.sp,
                    fontWeight = FontWeight.Bold,
                    color = Navy
                )
            }

            Text(
                text = "Verify Your Email",
                fontSize = 24.sp,
                fontWeight = FontWeight.Bold,
                color = Navy,
                modifier = Modifier.padding(top = 8.dp)
            )

            Text(
                text = "We've sent a verification link to your email address. " +
                        "Please check your inbox (and spam folder) and click the link to verify your account.",
                fontSize = 14.sp,
                color = Color.Gray,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(horizontal = 8.dp)
            )

            // ── Status indicator ────────────────────────────────────
            when (uiState) {
                is EmailVerifyUiState.Sending,
                is EmailVerifyUiState.Checking -> {
                    CircularProgressIndicator(
                        modifier = Modifier
                            .size(32.dp)
                            .padding(top = 8.dp),
                        color = Navy,
                        strokeWidth = 3.dp
                    )
                }

                is EmailVerifyUiState.EmailSent -> {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .background(Color(0xFFE8F5E9), RoundedCornerShape(8.dp))
                            .padding(12.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "Verification email sent! Check your inbox.",
                            fontSize = 13.sp,
                            color = Color(0xFF2E7D32),
                            fontWeight = FontWeight.Medium,
                            textAlign = TextAlign.Center
                        )
                    }
                }

                is EmailVerifyUiState.Error -> {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .background(Color(0xFFFFEBEE), RoundedCornerShape(8.dp))
                            .padding(12.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = (uiState as EmailVerifyUiState.Error).message,
                            fontSize = 13.sp,
                            color = RedPrimary,
                            fontWeight = FontWeight.Medium,
                            textAlign = TextAlign.Center
                        )
                    }
                }

                else -> { /* Pending or Verified -- no special indicator */ }
            }

            // ── "I've Verified My Email" button ─────────────────────
            Button(
                onClick = { viewModel.checkVerificationStatus() },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 16.dp),
                enabled = uiState !is EmailVerifyUiState.Checking &&
                        uiState !is EmailVerifyUiState.Sending,
                colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
            ) {
                Text(
                    "I've Verified My Email",
                    modifier = Modifier.padding(8.dp),
                    fontSize = 16.sp
                )
            }

            // ── Resend button ───────────────────────────────────────
            OutlinedButton(
                onClick = { viewModel.resendVerificationEmail() },
                modifier = Modifier.fillMaxWidth(),
                enabled = resendCooldown == 0 &&
                        uiState !is EmailVerifyUiState.Sending &&
                        uiState !is EmailVerifyUiState.Checking
            ) {
                Text(
                    text = if (resendCooldown > 0)
                        "Resend in ${resendCooldown}s"
                    else
                        "Resend Verification Email",
                    fontSize = 14.sp,
                    color = if (resendCooldown > 0) Color.Gray else Navy,
                    modifier = Modifier.padding(8.dp)
                )
            }

            // ── Help text ───────────────────────────────────────────
            Text(
                text = "Didn't receive the email? Check your spam folder or make sure you entered the correct email during signup.",
                fontSize = 12.sp,
                color = Color.Gray,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = 8.dp, start = 8.dp, end = 8.dp)
            )

            // ── Back to Login ───────────────────────────────────────
            TextButton(
                onClick = {
                    navController.navigate(NavRoute.Login.route) {
                        popUpTo(NavRoute.AuthGraph.route) { inclusive = true }
                    }
                },
                modifier = Modifier.padding(top = 16.dp, bottom = 32.dp)
            ) {
                Text(
                    text = "Back to Login",
                    fontSize = 14.sp,
                    color = Navy,
                    fontWeight = FontWeight.Bold
                )
            }
        }
    }
}
