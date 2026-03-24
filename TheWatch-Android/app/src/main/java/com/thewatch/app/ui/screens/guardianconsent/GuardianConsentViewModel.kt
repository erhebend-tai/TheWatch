/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         GuardianConsentViewModel.kt                            │
 * │ Purpose:      ViewModel for the Guardian Consent screen shown during │
 * │               signup when the user's DOB indicates they are under    │
 * │               18. Manages the consent request flow: submit guardian  │
 * │               contact info, send verification code, verify code.     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: GuardianConsentRepository, Hilt                        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val viewModel: GuardianConsentViewModel = hiltViewModel()          │
 * │   viewModel.submitGuardianInfo("Jane Doe", "jane@x.com",            │
 * │       "+1-555-0100", "Mother")                                       │
 * │   // After guardian receives code:                                   │
 * │   viewModel.verifyCode("WATCH123")                                   │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.guardianconsent

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.repository.ConsentStatus
import com.thewatch.app.data.repository.GuardianConsentRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class GuardianConsentViewModel @Inject constructor(
    private val guardianConsentRepository: GuardianConsentRepository,
    private val savedStateHandle: SavedStateHandle
) : ViewModel() {

    private val _uiState = MutableStateFlow(GuardianConsentUiState())
    val uiState: StateFlow<GuardianConsentUiState> = _uiState.asStateFlow()

    /** The minor user's temporary ID (from sign-up flow). */
    private var minorUserId: String = "pending_${System.currentTimeMillis()}"

    /** The active consent request ID, set after submitGuardianInfo(). */
    private var activeRequestId: String? = null

    fun setMinorUserId(userId: String) {
        minorUserId = userId
    }

    fun submitGuardianInfo(
        guardianName: String,
        guardianEmail: String,
        guardianPhone: String,
        relationship: String
    ) {
        if (guardianName.isBlank() || guardianEmail.isBlank() || guardianPhone.isBlank()) {
            _uiState.value = _uiState.value.copy(
                errorMessage = "All guardian fields are required"
            )
            return
        }

        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(
                isLoading = true,
                errorMessage = null
            )

            val result = guardianConsentRepository.requestConsent(
                minorUserId = minorUserId,
                guardianName = guardianName,
                guardianEmail = guardianEmail,
                guardianPhone = guardianPhone,
                relationship = relationship
            )

            result.fold(
                onSuccess = { request ->
                    activeRequestId = request.id
                    _uiState.value = _uiState.value.copy(
                        isLoading = false,
                        step = ConsentStep.VERIFY_CODE,
                        guardianName = guardianName,
                        guardianEmail = guardianEmail
                    )
                },
                onFailure = { e ->
                    _uiState.value = _uiState.value.copy(
                        isLoading = false,
                        errorMessage = e.message ?: "Failed to send consent request"
                    )
                }
            )
        }
    }

    fun verifyCode(code: String) {
        val requestId = activeRequestId
        if (requestId == null) {
            _uiState.value = _uiState.value.copy(
                errorMessage = "No active consent request. Please go back and retry."
            )
            return
        }

        if (code.isBlank()) {
            _uiState.value = _uiState.value.copy(
                errorMessage = "Please enter the verification code"
            )
            return
        }

        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(
                isLoading = true,
                errorMessage = null
            )

            val result = guardianConsentRepository.verifyConsent(requestId, code)

            result.fold(
                onSuccess = { verified ->
                    if (verified) {
                        _uiState.value = _uiState.value.copy(
                            isLoading = false,
                            step = ConsentStep.COMPLETED,
                            consentGranted = true
                        )
                    } else {
                        _uiState.value = _uiState.value.copy(
                            isLoading = false,
                            errorMessage = "Invalid verification code. Please try again."
                        )
                    }
                },
                onFailure = { e ->
                    _uiState.value = _uiState.value.copy(
                        isLoading = false,
                        errorMessage = e.message ?: "Verification failed"
                    )
                }
            )
        }
    }

    fun resendCode() {
        val requestId = activeRequestId ?: return

        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true, errorMessage = null)

            val result = guardianConsentRepository.resendVerificationCode(requestId)

            result.fold(
                onSuccess = {
                    _uiState.value = _uiState.value.copy(
                        isLoading = false,
                        errorMessage = null,
                        codeResent = true
                    )
                },
                onFailure = { e ->
                    _uiState.value = _uiState.value.copy(
                        isLoading = false,
                        errorMessage = e.message ?: "Failed to resend code"
                    )
                }
            )
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(errorMessage = null)
    }
}

data class GuardianConsentUiState(
    val step: ConsentStep = ConsentStep.ENTER_GUARDIAN_INFO,
    val isLoading: Boolean = false,
    val guardianName: String = "",
    val guardianEmail: String = "",
    val consentGranted: Boolean = false,
    val codeResent: Boolean = false,
    val errorMessage: String? = null
)

enum class ConsentStep {
    ENTER_GUARDIAN_INFO,
    VERIFY_CODE,
    COMPLETED
}
