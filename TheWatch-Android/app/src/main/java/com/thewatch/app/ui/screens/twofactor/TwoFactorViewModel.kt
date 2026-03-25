package com.thewatch.app.ui.screens.twofactor

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.repository.AuthRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * ViewModel for the Two-Factor Authentication verification screen.
 *
 * Shown after login when the user's account has MFA enabled.
 * Supports three verification methods:
 *   - TOTP: 6-digit code from an authenticator app (Google Authenticator, Authy, etc.)
 *   - SMS:  6-digit code sent via text message to the user's registered phone
 *   - Backup: One-time recovery code (8-10 alphanumeric characters)
 *
 * Write-Ahead Log:
 *   - WAL Entry: MFA_SCREEN_OPEN        -> userId, timestamp
 *   - WAL Entry: MFA_METHOD_SWITCH       -> fromMethod, toMethod, timestamp
 *   - WAL Entry: MFA_CODE_SUBMIT         -> method, codeLength, timestamp
 *   - WAL Entry: MFA_VERIFY_SUCCESS      -> method, timestamp
 *   - WAL Entry: MFA_VERIFY_FAIL         -> method, errorMsg, timestamp
 *   - WAL Entry: MFA_SMS_RESEND          -> timestamp
 *   - WAL Entry: MFA_SMS_COOLDOWN_EXPIRE -> timestamp
 *
 * Example usage:
 *   // In navigation, after login returns user.mfaEnabled == true:
 *   navController.navigate(NavRoute.TwoFactor.route)
 *
 *   // In TwoFactorScreen composable:
 *   val viewModel: TwoFactorViewModel = hiltViewModel()
 *   viewModel.verifyCode("123456")
 */
@HiltViewModel
class TwoFactorViewModel @Inject constructor(
    private val authRepository: AuthRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow<TwoFactorUiState>(TwoFactorUiState.Idle)
    val uiState: StateFlow<TwoFactorUiState> = _uiState.asStateFlow()

    private val _selectedMethod = MutableStateFlow(MfaMethod.TOTP)
    val selectedMethod: StateFlow<MfaMethod> = _selectedMethod.asStateFlow()

    private val _smsResendCooldown = MutableStateFlow(0)
    val smsResendCooldown: StateFlow<Int> = _smsResendCooldown.asStateFlow()

    private var countdownJob: Job? = null

    /**
     * Verify the user-entered MFA code against the backend.
     * On success, the backend marks the session as fully authenticated.
     */
    fun verifyCode(code: String) {
        if (code.isEmpty()) {
            _uiState.value = TwoFactorUiState.Error("Please enter the verification code.")
            return
        }

        val method = _selectedMethod.value
        if (method != MfaMethod.BACKUP && code.length != 6) {
            _uiState.value = TwoFactorUiState.Error("Code must be 6 digits.")
            return
        }

        viewModelScope.launch {
            _uiState.value = TwoFactorUiState.Loading
            val result = authRepository.verifyMfaCode(
                code = code,
                method = method.apiValue
            )
            result.fold(
                onSuccess = {
                    _uiState.value = TwoFactorUiState.Success
                },
                onFailure = { e ->
                    _uiState.value = TwoFactorUiState.Error(
                        e.message ?: "Verification failed. Please try again."
                    )
                }
            )
        }
    }

    /**
     * Switch between MFA methods (TOTP, SMS, Backup).
     * If switching to SMS, triggers an SMS send with cooldown.
     */
    fun selectMethod(method: MfaMethod) {
        _selectedMethod.value = method
        _uiState.value = TwoFactorUiState.Idle
        if (method == MfaMethod.SMS && _smsResendCooldown.value == 0) {
            resendSmsCode()
        }
    }

    /**
     * Request the backend to resend an SMS verification code.
     * Starts a 60-second cooldown to prevent abuse.
     */
    fun resendSmsCode() {
        if (_smsResendCooldown.value > 0) return

        viewModelScope.launch {
            _uiState.value = TwoFactorUiState.Loading
            val result = authRepository.verifyMfaCode(
                code = "RESEND",
                method = "sms_resend"
            )
            result.fold(
                onSuccess = {
                    _uiState.value = TwoFactorUiState.SmsSent
                    startResendCooldown()
                },
                onFailure = { e ->
                    _uiState.value = TwoFactorUiState.Error(
                        e.message ?: "Failed to resend SMS. Try again."
                    )
                }
            )
        }
    }

    private fun startResendCooldown() {
        countdownJob?.cancel()
        _smsResendCooldown.value = 60
        countdownJob = viewModelScope.launch {
            while (_smsResendCooldown.value > 0) {
                delay(1000)
                _smsResendCooldown.value -= 1
            }
        }
    }

    fun resetUiState() {
        _uiState.value = TwoFactorUiState.Idle
    }
}

/**
 * MFA verification methods.
 * Each maps to an API method string used by POST /api/account/mfa/verify.
 */
enum class MfaMethod(val apiValue: String, val displayName: String) {
    TOTP("totp", "Authenticator App"),
    SMS("sms", "Text Message"),
    BACKUP("backup", "Backup Code")
}

sealed class TwoFactorUiState {
    object Idle : TwoFactorUiState()
    object Loading : TwoFactorUiState()
    object Success : TwoFactorUiState()
    object SmsSent : TwoFactorUiState()
    data class Error(val message: String) : TwoFactorUiState()
}
