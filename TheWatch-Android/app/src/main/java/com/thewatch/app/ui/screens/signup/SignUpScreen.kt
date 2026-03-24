package com.thewatch.app.ui.screens.signup

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
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
import androidx.compose.material3.Checkbox
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
import com.thewatch.app.ui.components.PasswordStrengthMeter
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import kotlinx.coroutines.launch

@Composable
fun SignUpScreen(
    navController: NavController,
    viewModel: SignUpViewModel = hiltViewModel()
) {
    var step by remember { mutableStateOf(1) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var confirmPassword by remember { mutableStateOf("") }
    var showPassword by remember { mutableStateOf(false) }
    var showConfirmPassword by remember { mutableStateOf(false) }
    var fullName by remember { mutableStateOf("") }
    var dateOfBirth by remember { mutableStateOf("") }
    var phoneNumber by remember { mutableStateOf("") }
    
    var contactName by remember { mutableStateOf("") }
    var contactPhone by remember { mutableStateOf("") }
    var contactEmail by remember { mutableStateOf("") }
    var contactRelationship by remember { mutableStateOf("") }
    
    var eulaAccepted by remember { mutableStateOf(false) }
    var eulaScrolledToBottom by remember { mutableStateOf(false) }
    
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
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = "TheWatch",
                fontSize = 28.sp,
                fontWeight = FontWeight.Bold,
                color = Navy,
                modifier = Modifier.padding(bottom = 8.dp)
            )

            Text(
                text = "Create Your Account",
                fontSize = 18.sp,
                fontWeight = FontWeight.SemiBold,
                color = Navy,
                modifier = Modifier.padding(bottom = 24.dp)
            )

            ProgressIndicator(currentStep = step, totalSteps = 3)

            when (step) {
                1 -> StepOneAccountDetails(
                    email = email,
                    onEmailChange = { email = it },
                    password = password,
                    onPasswordChange = { password = it },
                    showPassword = showPassword,
                    onShowPasswordChange = { showPassword = !showPassword },
                    confirmPassword = confirmPassword,
                    onConfirmPasswordChange = { confirmPassword = it },
                    showConfirmPassword = showConfirmPassword,
                    onShowConfirmPasswordChange = { showConfirmPassword = !showConfirmPassword },
                    fullName = fullName,
                    onFullNameChange = { fullName = it },
                    dateOfBirth = dateOfBirth,
                    onDateOfBirthChange = { dateOfBirth = it },
                    phoneNumber = phoneNumber,
                    onPhoneNumberChange = { phoneNumber = it }
                )

                2 -> StepTwoEmergencyContact(
                    contactName = contactName,
                    onContactNameChange = { contactName = it },
                    contactPhone = contactPhone,
                    onContactPhoneChange = { contactPhone = it },
                    contactEmail = contactEmail,
                    onContactEmailChange = { contactEmail = it },
                    contactRelationship = contactRelationship,
                    onContactRelationshipChange = { contactRelationship = it }
                )

                3 -> StepThreeEULA(
                    eulaAccepted = eulaAccepted,
                    onEulaAcceptedChange = { eulaAccepted = it },
                    onScrolledToBottom = { eulaScrolledToBottom = it }
                )
            }

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 32.dp),
                horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(12.dp)
            ) {
                if (step > 1) {
                    Button(
                        onClick = { step-- },
                        modifier = Modifier
                            .weight(1f),
                        colors = ButtonDefaults.buttonColors(containerColor = Color.Gray)
                    ) {
                        Text("Back", modifier = Modifier.padding(8.dp), fontSize = 14.sp)
                    }
                }

                Button(
                    onClick = {
                        if (step < 3) {
                            step++
                        } else {
                            isLoading = true
                            scope.launch {
                                viewModel.signUp(email, password, fullName, phoneNumber)
                                isLoading = false
                                navController.navigate(NavRoute.Login.route) {
                                    popUpTo(NavRoute.SignUp.route) { inclusive = true }
                                }
                            }
                        }
                    },
                    modifier = Modifier.weight(1f),
                    enabled = isStepValid(step, email, password, confirmPassword, fullName, dateOfBirth, phoneNumber, contactName, contactPhone, contactEmail, eulaAccepted, eulaScrolledToBottom) && !isLoading,
                    colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
                ) {
                    Text(
                        if (step == 3) "Create Account" else "Next",
                        modifier = Modifier.padding(8.dp),
                        fontSize = 14.sp
                    )
                }
            }

            if (step == 1) {
                Text(
                    text = "Already have an account? Login",
                    fontSize = 14.sp,
                    color = Navy,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier
                        .clickable { navController.navigate(NavRoute.Login.route) }
                        .padding(top = 16.dp)
                )
            }
        }
    }
}

@Composable
private fun ProgressIndicator(currentStep: Int, totalSteps: Int) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 24.dp),
        horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(8.dp)
    ) {
        repeat(totalSteps) { index ->
            Box(
                modifier = Modifier
                    .weight(1f)
                    .height(4.dp)
                    .background(
                        color = if (index < currentStep) RedPrimary else Color.LightGray,
                        shape = RoundedCornerShape(2.dp)
                    )
            )
        }
    }
}

@Composable
private fun StepOneAccountDetails(
    email: String,
    onEmailChange: (String) -> Unit,
    password: String,
    onPasswordChange: (String) -> Unit,
    showPassword: Boolean,
    onShowPasswordChange: () -> Unit,
    confirmPassword: String,
    onConfirmPasswordChange: (String) -> Unit,
    showConfirmPassword: Boolean,
    onShowConfirmPasswordChange: () -> Unit,
    fullName: String,
    onFullNameChange: (String) -> Unit,
    dateOfBirth: String,
    onDateOfBirthChange: (String) -> Unit,
    phoneNumber: String,
    onPhoneNumberChange: (String) -> Unit
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        OutlinedTextField(
            value = fullName,
            onValueChange = onFullNameChange,
            label = { Text("Full Name") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )

        OutlinedTextField(
            value = email,
            onValueChange = onEmailChange,
            label = { Text("Email") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
            singleLine = true
        )

        OutlinedTextField(
            value = phoneNumber,
            onValueChange = onPhoneNumberChange,
            label = { Text("Phone Number") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone),
            singleLine = true
        )

        OutlinedTextField(
            value = dateOfBirth,
            onValueChange = onDateOfBirthChange,
            label = { Text("Date of Birth (MM/DD/YYYY)") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            singleLine = true
        )

        OutlinedTextField(
            value = password,
            onValueChange = onPasswordChange,
            label = { Text("Password") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
            trailingIcon = {
                IconButton(onClick = onShowPasswordChange) {
                    Icon(
                        imageVector = if (showPassword) Icons.Filled.Visibility else Icons.Filled.VisibilityOff,
                        contentDescription = "Toggle password visibility"
                    )
                }
            },
            singleLine = true
        )

        PasswordStrengthMeter(password = password)

        OutlinedTextField(
            value = confirmPassword,
            onValueChange = onConfirmPasswordChange,
            label = { Text("Confirm Password") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            visualTransformation = if (showConfirmPassword) VisualTransformation.None else PasswordVisualTransformation(),
            trailingIcon = {
                IconButton(onClick = onShowConfirmPasswordChange) {
                    Icon(
                        imageVector = if (showConfirmPassword) Icons.Filled.Visibility else Icons.Filled.VisibilityOff,
                        contentDescription = "Toggle password visibility"
                    )
                }
            },
            singleLine = true
        )
    }
}

@Composable
private fun StepTwoEmergencyContact(
    contactName: String,
    onContactNameChange: (String) -> Unit,
    contactPhone: String,
    onContactPhoneChange: (String) -> Unit,
    contactEmail: String,
    onContactEmailChange: (String) -> Unit,
    contactRelationship: String,
    onContactRelationshipChange: (String) -> Unit
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = "Add Your First Emergency Contact",
            fontSize = 16.sp,
            fontWeight = FontWeight.SemiBold,
            color = Navy,
            modifier = Modifier.padding(bottom = 16.dp)
        )

        OutlinedTextField(
            value = contactName,
            onValueChange = onContactNameChange,
            label = { Text("Contact Name") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )

        OutlinedTextField(
            value = contactRelationship,
            onValueChange = onContactRelationshipChange,
            label = { Text("Relationship") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            singleLine = true
        )

        OutlinedTextField(
            value = contactPhone,
            onValueChange = onContactPhoneChange,
            label = { Text("Phone Number") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone),
            singleLine = true
        )

        OutlinedTextField(
            value = contactEmail,
            onValueChange = onContactEmailChange,
            label = { Text("Email") },
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
            singleLine = true
        )

        Text(
            text = "You can add more contacts after account creation",
            fontSize = 12.sp,
            color = Color.Gray,
            modifier = Modifier.padding(top = 16.dp)
        )
    }
}

@Composable
private fun StepThreeEULA(
    eulaAccepted: Boolean,
    onEulaAcceptedChange: (Boolean) -> Unit,
    onScrolledToBottom: (Boolean) -> Unit
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(300.dp)
                .background(Color(0xFFF5F5F5), RoundedCornerShape(8.dp))
                .padding(16.dp)
                .verticalScroll(rememberScrollState()) { scrollOffset ->
                    onScrolledToBottom(scrollOffset > 0.95f)
                }
        ) {
            Text(
                text = "End User License Agreement\n\n" +
                        "TheWatch is a life-safety emergency response application designed to connect users with emergency services and responders during critical situations.\n\n" +
                        "By using TheWatch, you agree to:\n" +
                        "1. Share your location data with emergency services and authorized responders\n" +
                        "2. Comply with all applicable laws and regulations\n" +
                        "3. Use the service only for legitimate emergency situations\n" +
                        "4. Not abuse the system or make false emergency reports\n" +
                        "5. Allow us to collect and process health and emergency contact information\n\n" +
                        "TheWatch makes reasonable efforts to provide accurate emergency services, but cannot guarantee response times or outcomes. Emergency situations should also be reported directly to emergency services (911 in the US) when necessary.\n\n" +
                        "Your privacy is important to us. We will never sell your personal or health information. All location data is encrypted and only shared with authorized emergency responders.\n\n" +
                        "This app is not a replacement for professional emergency services. In life-threatening situations, always call emergency services directly.\n\n" +
                        "TheWatch is provided as-is without warranties. We are not liable for any indirect or consequential damages arising from your use of the service.",
                fontSize = 12.sp,
                color = Color.DarkGray
            )
        }

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Checkbox(
                checked = eulaAccepted,
                onCheckedChange = onEulaAcceptedChange,
                modifier = Modifier.padding(end = 8.dp)
            )
            Text(
                text = "I agree to the Terms and Privacy Policy",
                fontSize = 14.sp,
                color = if (eulaAccepted) Navy else Color.Gray
            )
        }
    }
}

private fun isStepValid(
    step: Int,
    email: String,
    password: String,
    confirmPassword: String,
    fullName: String,
    dateOfBirth: String,
    phoneNumber: String,
    contactName: String,
    contactPhone: String,
    contactEmail: String,
    eulaAccepted: Boolean,
    eulaScrolledToBottom: Boolean
): Boolean {
    return when (step) {
        1 -> email.isNotEmpty() && password.isNotEmpty() && 
             confirmPassword.isNotEmpty() && password == confirmPassword &&
             fullName.isNotEmpty() && dateOfBirth.isNotEmpty() && phoneNumber.isNotEmpty() &&
             password.length >= 8
        2 -> contactName.isNotEmpty() && contactPhone.isNotEmpty() && 
             contactEmail.isNotEmpty() && contactRelationship.isNotEmpty()
        3 -> eulaAccepted && eulaScrolledToBottom
        else -> false
    }
}
