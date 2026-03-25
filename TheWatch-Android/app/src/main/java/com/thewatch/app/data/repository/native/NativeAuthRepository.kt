package com.thewatch.app.data.repository.native

import android.content.Context
import android.util.Log
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.thewatch.app.data.model.MfaEnrollmentChallenge
import com.thewatch.app.data.model.User
import com.thewatch.app.data.repository.AuthRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.UUID

/**
 * 3-Tier Architecture: Native Adapter Tier
 * Implementation of AuthRepository representing the "Works Without Internet" tier.
 * This utilizes local hardware capabilities (EncryptedSharedPreferences) to securely
 * persist the user's token and state while offline. Biometric integration happens here
 * or at the ViewModel level depending on Activity Context requirements.
 */
class NativeAuthRepository(
    private val context: Context
) : AuthRepository {

    private val currentUserFlow = MutableStateFlow<User?>(null)
    
    // Setting up EncryptedSharedPreferences for secure local token/password storage
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val sharedPreferences = EncryptedSharedPreferences.create(
        context,
        "secure_auth_prefs",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    init {
        // Hydrate from secure storage on startup
        val savedUserId = sharedPreferences.getString("auth_user_id", null)
        val savedName = sharedPreferences.getString("auth_user_name", null)
        if (savedUserId != null && savedName != null) {
            val user = User(
                id = savedUserId,
                firstName = savedName.split(" ").firstOrNull() ?: "",
                lastName = savedName.split(" ").drop(1).joinToString(" "),
                email = sharedPreferences.getString("auth_user_email", "") ?: "",
                phoneNumber = sharedPreferences.getString("auth_user_phone", "") ?: "",
                dateOfBirth = "",
                profilePictureUrl = null,
                bloodType = "Unknown",
                medicalConditions = emptyList(),
                allergies = emptyList(),
                emergencyContacts = emptyList()
            )
            currentUserFlow.value = user
        }
    }

    override suspend fun login(emailOrPhone: String, password: String): Result<User> {
        val storedEmail = sharedPreferences.getString("auth_user_email", null)
        val storedPassword = sharedPreferences.getString("auth_user_password", null)

        return if (emailOrPhone == storedEmail && password == storedPassword) {
            val user = User(
                id = sharedPreferences.getString("auth_user_id", UUID.randomUUID().toString())!!,
                firstName = sharedPreferences.getString("auth_user_name", "User").toString(),
                lastName = "",
                email = emailOrPhone,
                phoneNumber = "",
                dateOfBirth = "",
                profilePictureUrl = null,
                bloodType = "Unknown",
                medicalConditions = emptyList(),
                allergies = emptyList(),
                emergencyContacts = emptyList()
            )
            currentUserFlow.value = user
            Result.success(user)
        } else {
            Result.failure(Exception("Invalid credentials or no local account found."))
        }
    }

    override suspend fun signUp(
        name: String, email: String, phone: String, 
        dateOfBirth: String, password: String
    ): Result<User> {
        val userId = UUID.randomUUID().toString()
        val user = User(
            id = userId,
            firstName = name.split(" ").firstOrNull() ?: name,
            lastName = name.split(" ").drop(1).joinToString(" "),
            email = email,
            phoneNumber = phone,
            dateOfBirth = dateOfBirth,
            profilePictureUrl = null,
            bloodType = "Unknown",
            medicalConditions = emptyList(),
            allergies = emptyList(),
            emergencyContacts = emptyList(),
            hasAcceptedEula = false
        )

        // Persist local state
        sharedPreferences.edit().apply {
            putString("auth_user_id", userId)
            putString("auth_user_name", name)
            putString("auth_user_email", email)
            putString("auth_user_phone", phone)
            putString("auth_user_password", password) // securely encrypted
        }.apply()

        currentUserFlow.value = user
        return Result.success(user)
    }

    override suspend fun biometricLogin(): Result<User> {
        // Biometric UI must be invoked via FragmentActivity in ViewModel/View.
        // Once successfully verified by AndroidX BiometricPrompt, it calls this to hydrate state.
        val user = currentUserFlow.value
        return if (user != null) {
            Result.success(user)
        } else {
            Result.failure(Exception("No active account for biometric login."))
        }
    }

    override suspend fun logout(): Result<Unit> {
        sharedPreferences.edit().clear().apply()
        currentUserFlow.value = null
        return Result.success(Unit)
    }

    override fun getCurrentUser(): Flow<User?> = currentUserFlow.asStateFlow()

    override suspend fun acceptEULA(userId: String): Result<Unit> = Result.success(Unit)
    override suspend fun sendPasswordResetCode(emailOrPhone: String): Result<String> = Result.success("123456")
    override suspend fun verifyResetCode(code: String): Result<Boolean> = Result.success(true)
    override suspend fun resetPassword(code: String, newPassword: String): Result<Boolean> {
        sharedPreferences.edit().putString("auth_user_password", newPassword).apply()
        return Result.success(true)
    }

    // ── New interface methods (native stubs -- MFA and email verify require network) ──

    override suspend fun sendEmailVerification(): Result<Unit> =
        Result.failure(Exception("Email verification requires an internet connection."))

    override suspend fun refreshToken(): Result<User> {
        val user = currentUserFlow.value
            ?: return Result.failure(Exception("No user session."))
        return Result.success(user)
    }

    override suspend fun enrollMfa(method: String, phoneNumber: String?): Result<MfaEnrollmentChallenge> =
        Result.failure(Exception("MFA enrollment requires an internet connection."))

    override suspend fun confirmMfaEnrollment(sessionId: String, code: String): Result<Boolean> =
        Result.failure(Exception("MFA enrollment confirmation requires an internet connection."))

    override suspend fun verifyMfaCode(code: String, method: String): Result<Boolean> =
        Result.failure(Exception("MFA verification requires an internet connection."))
}
