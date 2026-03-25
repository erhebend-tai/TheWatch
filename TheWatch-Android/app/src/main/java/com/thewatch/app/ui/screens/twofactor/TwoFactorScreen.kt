package com.thewatch.app.ui.screens.twofactor

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.navigation.NavRoute
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

/**
 * Two-Factor Authentication verification screen.
 *
 * Displayed after login when user.mfaEnabled == true. Presents a 6-digit
 * OTP-style input with individual digit boxes, method selector tabs, and
 * a countdown timer for SMS resend.
 *
 * Write-Ahead Log:
 *   - WAL Entry: 2FA_SCREEN_RENDER   -> method, timestamp
 *   - WAL Entry: 2FA_CODE_INPUT      -> digitCount, timestamp
 *   - WAL Entry: 2FA_SUBMIT_TAP      -> method, codeLength, timestamp
 *   - WAL Entry: 2FA_METHOD_TAP      -> newMethod, timestamp
 *   - WAL Entry: 2FA_RESEND_TAP      -> cooldownRemaining, timestamp
 *   - WAL Entry: 2FA_SUCCESS_NAV     -> destination, timestamp
 *
 * Example navigation setup:
 *   composable(NavRoute.TwoFactor.route) {
 *       TwoFactorScreen(navController = navController)
 *   }
 */
@Composable
fun TwoFactorScreen(
    navController: NavController,
    viewModel: TwoFactorViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    val selectedMethod by viewModel.selectedMethod.collectAsState()
    val smsResendCooldown by viewModel.smsResendCooldown.collectAsState()

    var code by remember { mutableStateOf("") }
    val focusRequester = remember { FocusRequester() }

    // Navigate on successful verification
    LaunchedEffect(uiState) {
        if (uiState is TwoFactorUiState.Success) {
            navController.navigate(NavRoute.AppGraph.route) {
                popUpTo(NavRoute.AuthGraph.route) { inclusive = true }
            }
        }
    }

    // Auto-focus the code input on mount
    LaunchedEffect(Unit) {
        focusRequester.requestFocus()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(White)
            .verticalScroll(rememberScrollState()),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // ── Header ──────────────────────────────────────────────────
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Box(
                modifier = Modifier
                    .size(64.dp)
                    .background(color = Navy, shape = RoundedCornerShape(12.dp)),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "2FA",
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = White
                )
            }

            Text(
                text = "Two-Factor Verification",
                fontSize = 24.sp,
                fontWeight = FontWeight.Bold,
                color = Navy,
                modifier = Modifier.padding(top = 16.dp)
            )

            Text(
                text = when (selectedMethod) {
                    MfaMethod.TOTP -> "Enter the 6-digit code from your authenticator app"
                    MfaMethod.SMS -> "Enter the 6-digit code sent to your phone"
                    MfaMethod.BACKUP -> "Enter one of your backup recovery codes"
                },
                fontSize = 14.sp,
                color = Color.Gray,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = 8.dp, bottom = 24.dp)
            )
        }

        // ── Method Selector Tabs ────────────────────────────────────
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 24.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            MfaMethod.entries.forEach { method ->
                val isSelected = method == selectedMethod
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(40.dp)
                        .background(
                            color = if (isSelected) Navy else Color(0xFFF5F5F5),
                            shape = RoundedCornerShape(8.dp)
                        )
                        .clickable { viewModel.selectMethod(method); code = "" },
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = method.displayName,
                        fontSize = 11.sp,
                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                        color = if (isSelected) White else Navy,
                        textAlign = TextAlign.Center
                    )
                }
            }
        }

        // ── OTP Code Input ──────────────────────────────────────────
        if (selectedMethod != MfaMethod.BACKUP) {
            // 6 individual digit boxes
            OtpInputField(
                code = code,
                onCodeChange = { newCode ->
                    if (newCode.length <= 6 && newCode.all { it.isDigit() }) {
                        code = newCode
                    }
                },
                digitCount = 6,
                focusRequester = focusRequester,
                modifier = Modifier.padding(horizontal = 24.dp, vertical = 16.dp)
            )
        } else {
            // Backup code: freeform text input
            BasicTextField(
                value = code,
                onValueChange = { code = it.take(20) },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp, vertical = 16.dp)
                    .background(Color(0xFFF5F5F5), RoundedCornerShape(8.dp))
                    .padding(16.dp)
                    .focusRequester(focusRequester),
                textStyle = TextStyle(
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Medium,
                    color = Navy,
                    textAlign = TextAlign.Center,
                    letterSpacing = 2.sp
                ),
                singleLine = true
            )
        }

        // ── Error Message ───────────────────────────────────────────
        if (uiState is TwoFactorUiState.Error) {
            Text(
                text = (uiState as TwoFactorUiState.Error).message,
                fontSize = 13.sp,
                color = RedPrimary,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp, vertical = 8.dp)
            )
        }

        // ── SMS Sent Confirmation ───────────────────────────────────
        if (uiState is TwoFactorUiState.SmsSent) {
            Text(
                text = "A new code has been sent to your phone.",
                fontSize = 13.sp,
                color = Color(0xFF4CAF50),
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp, vertical = 8.dp)
            )
        }

        // ── Verify Button ───────────────────────────────────────────
        Button(
            onClick = { viewModel.verifyCode(code) },
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 24.dp, vertical = 8.dp),
            enabled = code.isNotEmpty() && uiState !is TwoFactorUiState.Loading,
            colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
        ) {
            if (uiState is TwoFactorUiState.Loading) {
                CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    color = White,
                    strokeWidth = 2.dp
                )
            } else {
                Text(
                    "Verify",
                    modifier = Modifier.padding(8.dp),
                    fontSize = 16.sp
                )
            }
        }

        // ── SMS Resend Button (only visible for SMS method) ─────────
        if (selectedMethod == MfaMethod.SMS) {
            TextButton(
                onClick = { viewModel.resendSmsCode() },
                enabled = smsResendCooldown == 0 && uiState !is TwoFactorUiState.Loading,
                modifier = Modifier.padding(top = 8.dp)
            ) {
                Text(
                    text = if (smsResendCooldown > 0)
                        "Resend code in ${smsResendCooldown}s"
                    else
                        "Resend SMS code",
                    fontSize = 14.sp,
                    color = if (smsResendCooldown > 0) Color.Gray else Navy,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        // ── Back to Login ───────────────────────────────────────────
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

/**
 * OTP-style input field rendering individual digit boxes.
 *
 * Each box shows one digit of the code. A hidden BasicTextField handles
 * actual keyboard input; the boxes are purely visual. The cursor highlight
 * moves to the next empty box.
 *
 * Example:
 *   OtpInputField(code = "12", onCodeChange = { ... }, digitCount = 6)
 *   // Renders: [1] [2] [ ] [ ] [ ] [ ]
 */
@Composable
private fun OtpInputField(
    code: String,
    onCodeChange: (String) -> Unit,
    digitCount: Int,
    focusRequester: FocusRequester,
    modifier: Modifier = Modifier
) {
    Box(modifier = modifier) {
        // Hidden text field for keyboard input
        BasicTextField(
            value = code,
            onValueChange = onCodeChange,
            modifier = Modifier
                .fillMaxWidth()
                .focusRequester(focusRequester)
                // Make the actual text field invisible; boxes render the digits
                .height(0.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
            singleLine = true
        )

        // Visual digit boxes
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            repeat(digitCount) { index ->
                val digit = code.getOrNull(index)?.toString() ?: ""
                val isFocused = index == code.length && code.length < digitCount

                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .background(
                            color = if (digit.isNotEmpty()) Color(0xFFF0F4FF) else Color(0xFFF5F5F5),
                            shape = RoundedCornerShape(8.dp)
                        )
                        .border(
                            width = if (isFocused) 2.dp else 1.dp,
                            color = when {
                                isFocused -> Navy
                                digit.isNotEmpty() -> Navy.copy(alpha = 0.3f)
                                else -> Color.LightGray
                            },
                            shape = RoundedCornerShape(8.dp)
                        )
                        .clickable { focusRequester.requestFocus() },
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = digit,
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy,
                        textAlign = TextAlign.Center
                    )
                }
            }
        }
    }
}
