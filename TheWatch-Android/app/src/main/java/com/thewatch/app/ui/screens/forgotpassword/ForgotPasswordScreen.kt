package com.thewatch.app.ui.screens.forgotpassword

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.navigation.NavRoute
import com.thewatch.app.ui.components.OfflineBanner
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import kotlinx.coroutines.launch

@Composable
fun ForgotPasswordScreen(
    navController: NavController,
    viewModel: ForgotPasswordViewModel = hiltViewModel()
) {
    var step by remember { mutableStateOf(1) }
    var emailOrPhone by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    var timeRemaining by remember { mutableStateOf(300) }
    var isLoading by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(step) {
        if (step == 2) {
            timeRemaining = 300
        }
    }

    LaunchedEffect(timeRemaining, step) {
        if (step == 2 && timeRemaining > 0) {
            kotlinx.coroutines.delay(1000)
            timeRemaining--
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(White)
            .verticalScroll(rememberScrollState())
    ) {
        OfflineBanner(isOnline = true)

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = "Reset Password",
                fontSize = 28.sp,
                fontWeight = FontWeight.Bold,
                color = Navy,
                modifier = Modifier.padding(bottom = 8.dp)
            )

            when (step) {
                1 -> {
                    Text(
                        text = "Enter your email or phone number",
                        fontSize = 14.sp,
                        color = Color.Gray,
                        modifier = Modifier.padding(bottom = 24.dp)
                    )

                    OutlinedTextField(
                        value = emailOrPhone,
                        onValueChange = { emailOrPhone = it },
                        label = { Text("Email or Phone") },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 24.dp),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                        singleLine = true
                    )

                    Button(
                        onClick = {
                            if (emailOrPhone.isNotEmpty()) {
                                isLoading = true
                                scope.launch {
                                    viewModel.sendPasswordResetCode(emailOrPhone)
                                    isLoading = false
                                    step = 2
                                }
                            }
                        },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 16.dp),
                        enabled = emailOrPhone.isNotEmpty() && !isLoading,
                        colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
                    ) {
                        Text("Send Reset Code", modifier = Modifier.padding(8.dp), fontSize = 16.sp)
                    }
                }

                2 -> {
                    Text(
                        text = "Enter the 6-digit code sent to your email/phone",
                        fontSize = 14.sp,
                        color = Color.Gray,
                        modifier = Modifier.padding(bottom = 24.dp)
                    )

                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .background(Color(0xFFE8F5E9), RoundedCornerShape(8.dp))
                            .padding(12.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "Code expires in ${timeRemaining / 60}:${String.format("%02d", timeRemaining % 60)}",
                            fontSize = 12.sp,
                            color = if (timeRemaining > 60) Color.Gray else RedPrimary,
                            fontWeight = FontWeight.SemiBold
                        )
                    }

                    OutlinedTextField(
                        value = code,
                        onValueChange = { if (it.length <= 6) code = it },
                        label = { Text("6-Digit Code") },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 16.dp, bottom = 24.dp),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                        singleLine = true
                    )

                    Button(
                        onClick = {
                            if (code.length == 6) {
                                isLoading = true
                                scope.launch {
                                    viewModel.verifyResetCode(emailOrPhone, code)
                                    isLoading = false
                                    step = 3
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        enabled = code.length == 6 && !isLoading && timeRemaining > 0,
                        colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
                    ) {
                        Text("Verify Code", modifier = Modifier.padding(8.dp), fontSize = 16.sp)
                    }

                    if (timeRemaining <= 0) {
                        Text(
                            text = "Code expired. Return to step 1.",
                            fontSize = 12.sp,
                            color = RedPrimary,
                            modifier = Modifier
                                .padding(top = 16.dp)
                                .align(Alignment.CenterHorizontally)
                        )

                        Button(
                            onClick = { step = 1; code = ""; emailOrPhone = "" },
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 16.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = Navy)
                        ) {
                            Text("Request New Code", modifier = Modifier.padding(8.dp), fontSize = 14.sp)
                        }
                    }
                }

                3 -> {
                    Text(
                        text = "Enter your new password",
                        fontSize = 14.sp,
                        color = Color.Gray,
                        modifier = Modifier.padding(bottom = 24.dp)
                    )

                    var newPassword by remember { mutableStateOf("") }
                    var confirmPassword by remember { mutableStateOf("") }

                    OutlinedTextField(
                        value = newPassword,
                        onValueChange = { newPassword = it },
                        label = { Text("New Password") },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 16.dp),
                        singleLine = true
                    )

                    OutlinedTextField(
                        value = confirmPassword,
                        onValueChange = { confirmPassword = it },
                        label = { Text("Confirm Password") },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 24.dp),
                        singleLine = true
                    )

                    Button(
                        onClick = {
                            if (newPassword.isNotEmpty() && newPassword == confirmPassword) {
                                isLoading = true
                                scope.launch {
                                    viewModel.resetPassword(emailOrPhone, code, newPassword)
                                    isLoading = false
                                    navController.navigate(NavRoute.Login.route) {
                                        popUpTo(NavRoute.ForgotPassword.route) { inclusive = true }
                                    }
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        enabled = newPassword.isNotEmpty() && newPassword == confirmPassword && newPassword.length >= 8 && !isLoading,
                        colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
                    ) {
                        Text("Reset Password", modifier = Modifier.padding(8.dp), fontSize = 16.sp)
                    }
                }
            }

            if (step > 1) {
                Button(
                    onClick = { step--; code = "" },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Color.Gray)
                ) {
                    Text("Back", modifier = Modifier.padding(8.dp), fontSize = 14.sp)
                }
            }

            Text(
                text = "Back to Login",
                fontSize = 14.sp,
                color = Navy,
                fontWeight = FontWeight.Bold,
                modifier = Modifier
                    .padding(top = 24.dp)
                    .align(Alignment.CenterHorizontally)
            )
        }
    }
}
