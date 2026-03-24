package com.thewatch.app.ui.screens.forgotpassword

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.repository.AuthRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class ForgotPasswordViewModel @Inject constructor(
    private val authRepository: AuthRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow<ForgotPasswordUiState>(ForgotPasswordUiState.Idle)
    val uiState: StateFlow<ForgotPasswordUiState> = _uiState.asStateFlow()

    fun sendPasswordResetCode(emailOrPhone: String) {
        if (emailOrPhone.isEmpty()) {
            _uiState.value = ForgotPasswordUiState.Error("Email or phone is required")
            return
        }

        viewModelScope.launch {
            _uiState.value = ForgotPasswordUiState.Loading
            try {
                authRepository.sendPasswordResetCode(emailOrPhone)
                _uiState.value = ForgotPasswordUiState.CodeSent
            } catch (e: Exception) {
                _uiState.value = ForgotPasswordUiState.Error(e.message ?: "Failed to send reset code")
            }
        }
    }

    fun verifyResetCode(emailOrPhone: String, code: String) {
        if (code.isEmpty() || code.length != 6) {
            _uiState.value = ForgotPasswordUiState.Error("Invalid code format")
            return
        }

        viewModelScope.launch {
            _uiState.value = ForgotPasswordUiState.Loading
            try {
                authRepository.verifyResetCode(emailOrPhone, code)
                _uiState.value = ForgotPasswordUiState.CodeVerified
            } catch (e: Exception) {
                _uiState.value = ForgotPasswordUiState.Error(e.message ?: "Invalid code")
            }
        }
    }

    fun resetPassword(emailOrPhone: String, code: String, newPassword: String) {
        if (newPassword.isEmpty() || newPassword.length < 8) {
            _uiState.value = ForgotPasswordUiState.Error("Password must be at least 8 characters")
            return
        }

        viewModelScope.launch {
            _uiState.value = ForgotPasswordUiState.Loading
            try {
                authRepository.resetPassword(emailOrPhone, code, newPassword)
                _uiState.value = ForgotPasswordUiState.Success
            } catch (e: Exception) {
                _uiState.value = ForgotPasswordUiState.Error(e.message ?: "Failed to reset password")
            }
        }
    }

    fun resetUiState() {
        _uiState.value = ForgotPasswordUiState.Idle
    }
}

sealed class ForgotPasswordUiState {
    object Idle : ForgotPasswordUiState()
    object Loading : ForgotPasswordUiState()
    object CodeSent : ForgotPasswordUiState()
    object CodeVerified : ForgotPasswordUiState()
    object Success : ForgotPasswordUiState()
    data class Error(val message: String) : ForgotPasswordUiState()
}
