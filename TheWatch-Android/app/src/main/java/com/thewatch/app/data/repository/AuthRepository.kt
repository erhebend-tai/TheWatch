package com.thewatch.app.data.repository

import com.thewatch.app.data.model.User
import kotlinx.coroutines.flow.Flow

interface AuthRepository {
    suspend fun login(emailOrPhone: String, password: String): Result<User>
    suspend fun signUp(
        name: String,
        email: String,
        phone: String,
        dateOfBirth: String,
        password: String
    ): Result<User>
    suspend fun sendPasswordResetCode(emailOrPhone: String): Result<String>
    suspend fun verifyResetCode(code: String): Result<Boolean>
    suspend fun resetPassword(code: String, newPassword: String): Result<Boolean>
    suspend fun biometricLogin(): Result<User>
    suspend fun logout(): Result<Unit>
    fun getCurrentUser(): Flow<User?>
    suspend fun acceptEULA(userId: String): Result<Unit>
}
