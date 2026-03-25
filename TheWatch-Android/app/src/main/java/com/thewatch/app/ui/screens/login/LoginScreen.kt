package com.thewatch.app.ui.screens.login

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
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
fun LoginScreen(
    navController: NavController,
    viewModel: LoginViewModel = hiltViewModel()
) {
    var emailOrPhone by remember { mutableStateOf("alex.rivera@example.com") }
    var password by remember { mutableStateOf("Password123!") }
    var showPassword by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

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
                .padding(24.dp)
                .weight(1f),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Box(
                modifier = Modifier
                    .size(80.dp)
                    .background(color = Navy, shape = RoundedCornerShape(16.dp))
                    .semantics { contentDescription = "TheWatch app logo" },
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "TW",
                    fontSize = 32.sp,
                    fontWeight = FontWeight.Bold,
                    color = White
                )
            }

            Text(
                text = "TheWatch",
                fontSize = 28.sp,
                fontWeight = FontWeight.Bold,
                color = Navy,
                modifier = Modifier
                    .padding(top = 16.dp)
                    .semantics { heading() }
            )

            Text(
                text = "Your Life, Protected",
                fontSize = 14.sp,
                color = Color.Gray,
                modifier = Modifier.padding(top = 8.dp)
            )

            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 32.dp)
            ) {
                OutlinedTextField(
                    value = emailOrPhone,
                    onValueChange = { emailOrPhone = it },
                    label = { Text("Email or Phone") },
                    modifier = Modifier
                        .fillMaxWidth()
                        .semantics { contentDescription = "Email or phone number" },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                    singleLine = true
                )

                OutlinedTextField(
                    value = password,
                    onValueChange = { password = it },
                    label = { Text("Password") },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 16.dp),
                    visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                    trailingIcon = {
                        IconButton(onClick = { showPassword = !showPassword }) {
                            Icon(
                                imageVector = if (showPassword) Icons.Filled.Visibility else Icons.Filled.VisibilityOff,
                                contentDescription = if (showPassword) "Hide password" else "Show password"
                            )
                        }
                    },
                    singleLine = true
                )

                Button(
                    onClick = {
                        isLoading = true
                        scope.launch {
                            viewModel.login(emailOrPhone, password)
                            isLoading = false
                            navController.navigate(NavRoute.AppGraph.route) {
                                popUpTo(NavRoute.AuthGraph.route) { inclusive = true }
                            }
                        }
                    },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 24.dp)
                        .defaultMinSize(minHeight = 48.dp)
                        .semantics {
                            contentDescription = if (isLoading) {
                                "Logging in, please wait"
                            } else if (emailOrPhone.isEmpty() || password.isEmpty()) {
                                "Log in. Email and password are required."
                            } else {
                                "Log in"
                            }
                        },
                    enabled = emailOrPhone.isNotEmpty() && password.isNotEmpty() && !isLoading,
                    colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
                ) {
                    Text(
                        if (isLoading) "Logging in..." else "Login",
                        modifier = Modifier.padding(8.dp),
                        fontSize = 16.sp
                    )
                }

                Button(
                    onClick = { },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Navy)
                ) {
                    Text("Login with Biometric", modifier = Modifier.padding(8.dp), fontSize = 14.sp)
                }
            }

            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 32.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = "Forgot Password?",
                    fontSize = 14.sp,
                    color = RedPrimary,
                    modifier = Modifier
                        .clickable { navController.navigate(NavRoute.ForgotPassword.route) }
                        .padding(8.dp)
                        .defaultMinSize(minHeight = 48.dp)
                        .semantics { contentDescription = "Forgot password? Tap to reset." }
                )

                Text(
                    text = "Sign Up",
                    fontSize = 14.sp,
                    color = Navy,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier
                        .clickable { navController.navigate(NavRoute.SignUp.route) }
                        .padding(8.dp)
                        .defaultMinSize(minHeight = 48.dp)
                        .semantics { contentDescription = "Create new account" }
                )
            }
        }

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(bottom = 16.dp),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = "Hardware SOS Bypass",
                fontSize = 12.sp,
                color = Color.Gray,
                modifier = Modifier
                    .clickable { }
                    .padding(8.dp)
            )
        }
    }
}
