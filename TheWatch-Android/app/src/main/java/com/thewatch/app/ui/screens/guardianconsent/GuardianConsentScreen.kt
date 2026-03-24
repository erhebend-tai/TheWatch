/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         GuardianConsentScreen.kt                               │
 * │ Purpose:      Compose screen for guardian/parental consent during    │
 * │               signup of a user under 18. Two-step flow:             │
 * │               (1) Enter guardian contact info + relationship,        │
 * │               (2) Enter verification code sent to guardian.          │
 * │               On success, signup proceeds. On failure, account is    │
 * │               not created until consent is obtained.                 │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: GuardianConsentViewModel, Hilt, NavController, M3     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In NavGraph.kt authGraph:                                       │
 * │   composable(NavRoute.GuardianConsent.route) {                       │
 * │       GuardianConsentScreen(navController = navController)            │
 * │   }                                                                  │
 * │                                                                      │
 * │ COPPA/GDPR-K compliance notes:                                       │
 * │   - Consent must be "verifiable" — email/SMS code meets FTC reqs.   │
 * │   - Must explain what data is collected and why.                     │
 * │   - Guardian can revoke consent at any time (handled in Profile).    │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.guardianconsent

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

@Composable
fun GuardianConsentScreen(
    navController: NavController,
    viewModel: GuardianConsentViewModel = hiltViewModel()
) {
    val state by viewModel.uiState.collectAsState()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(White)
            .verticalScroll(rememberScrollState()),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // Top bar
        IconButton(
            onClick = { navController.popBackStack() },
            modifier = Modifier
                .align(Alignment.Start)
                .padding(16.dp)
        ) {
            Icon(
                imageVector = Icons.Filled.ArrowBack,
                contentDescription = "Go back",
                tint = Navy
            )
        }

        Text(
            text = "Guardian Consent Required",
            fontSize = 22.sp,
            fontWeight = FontWeight.Bold,
            color = Navy,
            modifier = Modifier.padding(horizontal = 24.dp)
        )

        Text(
            text = "Because you are under 18, a parent or legal guardian must provide consent before you can use TheWatch.",
            fontSize = 14.sp,
            color = Color.Gray,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(horizontal = 24.dp, vertical = 12.dp)
        )

        Spacer(modifier = Modifier.height(8.dp))

        when (state.step) {
            ConsentStep.ENTER_GUARDIAN_INFO -> GuardianInfoStep(
                isLoading = state.isLoading,
                errorMessage = state.errorMessage,
                onSubmit = { name, email, phone, relationship ->
                    viewModel.submitGuardianInfo(name, email, phone, relationship)
                }
            )

            ConsentStep.VERIFY_CODE -> VerifyCodeStep(
                guardianName = state.guardianName,
                guardianEmail = state.guardianEmail,
                isLoading = state.isLoading,
                codeResent = state.codeResent,
                errorMessage = state.errorMessage,
                onVerify = { code -> viewModel.verifyCode(code) },
                onResend = { viewModel.resendCode() }
            )

            ConsentStep.COMPLETED -> ConsentCompletedStep(
                onContinue = {
                    navController.popBackStack()
                }
            )
        }
    }
}

@Composable
private fun GuardianInfoStep(
    isLoading: Boolean,
    errorMessage: String?,
    onSubmit: (String, String, String, String) -> Unit
) {
    var guardianName by remember { mutableStateOf("") }
    var guardianEmail by remember { mutableStateOf("") }
    var guardianPhone by remember { mutableStateOf("") }
    var relationship by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(24.dp)
    ) {
        Text(
            text = "Step 1: Guardian Information",
            fontSize = 16.sp,
            fontWeight = FontWeight.SemiBold,
            color = Navy,
            modifier = Modifier.padding(bottom = 16.dp)
        )

        OutlinedTextField(
            value = guardianName,
            onValueChange = { guardianName = it },
            label = { Text("Guardian Full Name") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            enabled = !isLoading
        )

        OutlinedTextField(
            value = relationship,
            onValueChange = { relationship = it },
            label = { Text("Relationship (Parent, Legal Guardian, etc.)") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 12.dp),
            singleLine = true,
            enabled = !isLoading
        )

        OutlinedTextField(
            value = guardianEmail,
            onValueChange = { guardianEmail = it },
            label = { Text("Guardian Email") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 12.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
            singleLine = true,
            enabled = !isLoading
        )

        OutlinedTextField(
            value = guardianPhone,
            onValueChange = { guardianPhone = it },
            label = { Text("Guardian Phone Number") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 12.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone),
            singleLine = true,
            enabled = !isLoading
        )

        errorMessage?.let {
            Text(
                text = it,
                fontSize = 13.sp,
                color = Color.Red,
                modifier = Modifier.padding(top = 8.dp)
            )
        }

        Text(
            text = "A verification code will be sent to your guardian's email and phone. They must enter it to confirm consent.",
            fontSize = 12.sp,
            color = Color.Gray,
            modifier = Modifier.padding(top = 16.dp)
        )

        Button(
            onClick = { onSubmit(guardianName, guardianEmail, guardianPhone, relationship) },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 24.dp),
            enabled = guardianName.isNotBlank() && guardianEmail.isNotBlank() &&
                    guardianPhone.isNotBlank() && relationship.isNotBlank() && !isLoading,
            colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
        ) {
            Text(
                text = if (isLoading) "Sending..." else "Send Verification Code",
                modifier = Modifier.padding(8.dp),
                fontSize = 14.sp
            )
        }
    }
}

@Composable
private fun VerifyCodeStep(
    guardianName: String,
    guardianEmail: String,
    isLoading: Boolean,
    codeResent: Boolean,
    errorMessage: String?,
    onVerify: (String) -> Unit,
    onResend: () -> Unit
) {
    var verificationCode by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(
            text = "Step 2: Enter Verification Code",
            fontSize = 16.sp,
            fontWeight = FontWeight.SemiBold,
            color = Navy,
            modifier = Modifier.padding(bottom = 8.dp)
        )

        Text(
            text = "A code has been sent to $guardianName at $guardianEmail. " +
                    "Please ask your guardian to share the code with you.",
            fontSize = 13.sp,
            color = Color.Gray,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(bottom = 24.dp)
        )

        OutlinedTextField(
            value = verificationCode,
            onValueChange = { verificationCode = it.uppercase() },
            label = { Text("Verification Code") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            enabled = !isLoading
        )

        errorMessage?.let {
            Text(
                text = it,
                fontSize = 13.sp,
                color = Color.Red,
                modifier = Modifier.padding(top = 8.dp)
            )
        }

        if (codeResent) {
            Text(
                text = "Code resent successfully",
                fontSize = 13.sp,
                color = Color(0xFF4CAF50),
                modifier = Modifier.padding(top = 8.dp)
            )
        }

        Button(
            onClick = { onVerify(verificationCode) },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 24.dp),
            enabled = verificationCode.isNotBlank() && !isLoading,
            colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
        ) {
            Text(
                text = if (isLoading) "Verifying..." else "Verify Code",
                modifier = Modifier.padding(8.dp),
                fontSize = 14.sp
            )
        }

        Text(
            text = "Didn't receive a code? Resend",
            fontSize = 13.sp,
            color = Navy,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier
                .clickable { onResend() }
                .padding(top = 16.dp)
        )
    }
}

@Composable
private fun ConsentCompletedStep(
    onContinue: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Icon(
            imageVector = Icons.Filled.CheckCircle,
            contentDescription = "Consent granted",
            tint = Color(0xFF4CAF50),
            modifier = Modifier
                .padding(top = 32.dp)
                .then(Modifier.padding(16.dp))
        )

        Text(
            text = "Guardian Consent Granted",
            fontSize = 20.sp,
            fontWeight = FontWeight.Bold,
            color = Navy,
            modifier = Modifier.padding(top = 16.dp)
        )

        Text(
            text = "Your guardian has approved your account. You can now continue with sign-up.",
            fontSize = 14.sp,
            color = Color.Gray,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = 12.dp)
        )

        Button(
            onClick = onContinue,
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 32.dp),
            colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
        ) {
            Text(
                text = "Continue Sign Up",
                modifier = Modifier.padding(8.dp),
                fontSize = 14.sp
            )
        }
    }
}
