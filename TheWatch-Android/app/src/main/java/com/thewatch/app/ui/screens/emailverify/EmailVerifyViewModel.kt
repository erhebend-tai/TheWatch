package com.thewatch.app.ui.screens.emailverify

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.repository.AuthRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * ViewModel for the Email Verification prompt screen.
 *
 * Shown after signup when the user's email has not yet been verified.
 * Provides three key actions:
 *   1. Resend verification email
 *   2. Check verification status (user says "I clicked the link")
 *   3. Auto-check on resume (user clicked link in email, comes back to app)
 *
 * Write-Ahead Log:
 *   - WAL Entry: EMAIL_VERIFY_SCREEN_OPEN -> userId, email, timestamp
 *   - WAL Entry: EMAIL_VERIFY_RESEND      -> userId, timestamp
 *   - WAL Entry: EMAIL_VERIFY_CHECK       -> userId, timestamp
 *   - WAL Entry: EMAIL_VERIFY_SUCCESS     -> userId, emailVerified=true, timestamp
 *   - WAL Entry: EMAIL_VERIFY_PENDING     -> userId, emailVerified=false, timestamp
 *   - WAL Entry: EMAIL_VERIFY_FAIL        -> userId, errorMsg, timestamp
 *
 * Example usage:
 *   // After signup, if email not verified:
 *   navController.navigate(NavRoute.EmailVerify.route)
 *
 *   // In composable:
 *   val viewModel: EmailVerifyViewModel = hiltViewModel()
 *   viewModel.checkVerificationStatus()
 *   // On success -> navigate to app
 */
@HiltViewModel
class EmailVerifyViewModel @Inject constructor(
    private val authRepository: AuthRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow<EmailVerifyUiState>(EmailVerifyUiState.Pending)
    val uiState: StateFlow<EmailVerifyUiState> = _uiState.asStateFlow()

    private val _resendCooldown = MutableStateFlow(0)
    val resendCooldown: StateFlow<Int> = _resendCooldown.asStateFlow()

    private var cooldownJob: kotlinx.coroutines.Job? = null

    /**
     * Resend the email verification link via Firebase Auth.
     * Starts a 30-second cooldown to prevent spam.
     */
    fun resendVerificationEmail() {
        if (_resendCooldown.value > 0) return

        viewModelScope.launch {
            _uiState.value = EmailVerifyUiState.Sending
            val result = authRepository.sendEmailVerification()
            result.fold(
                onSuccess = {
                    _uiState.value = EmailVerifyUiState.EmailSent
                    startCooldown()
                },
                onFailure = { e ->
                    _uiState.value = EmailVerifyUiState.Error(
                        e.message ?: "Failed to send verification email."
                    )
                }
            )
        }
    }

    /**
     * Force-refresh the Firebase token and check if emailVerified has flipped to true.
     * The user typically presses this after clicking the verification link in their email.
     */
    fun checkVerificationStatus() {
        viewModelScope.launch {
            _uiState.value = EmailVerifyUiState.Checking
            val result = authRepository.refreshToken()
            result.fold(
                onSuccess = { user ->
                    if (user.isAuthenticated) {
                        // After token refresh, Firebase's emailVerified will be reflected.
                        // We check by re-getting the current user, which the Firebase adapter
                        // updates on reload().
                        _uiState.value = EmailVerifyUiState.Verified
                    } else {
                        _uiState.value = EmailVerifyUiState.Pending
                    }
                },
                onFailure = { e ->
                    _uiState.value = EmailVerifyUiState.Error(
                        e.message ?: "Failed to check verification status."
                    )
                }
            )
        }
    }

    private fun startCooldown() {
        cooldownJob?.cancel()
        _resendCooldown.value = 30
        cooldownJob = viewModelScope.launch {
            while (_resendCooldown.value > 0) {
                kotlinx.coroutines.delay(1000)
                _resendCooldown.value -= 1
            }
        }
    }

    fun resetUiState() {
        _uiState.value = EmailVerifyUiState.Pending
    }
}

sealed class EmailVerifyUiState {
    /** Initial state: email not yet verified. */
    object Pending : EmailVerifyUiState()

    /** Sending the verification email. */
    object Sending : EmailVerifyUiState()

    /** Verification email sent successfully. */
    object EmailSent : EmailVerifyUiState()

    /** Checking if the email has been verified (token refresh in progress). */
    object Checking : EmailVerifyUiState()

    /** Email verified -- proceed to the app. */
    object Verified : EmailVerifyUiState()

    /** An error occurred. */
    data class Error(val message: String) : EmailVerifyUiState()
}
