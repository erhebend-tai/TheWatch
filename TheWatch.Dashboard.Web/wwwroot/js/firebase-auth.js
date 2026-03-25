// =============================================================================
// firebase-auth.js — Firebase Auth interop for Blazor SSR
// =============================================================================
// Loaded in App.razor. Blazor calls these via IJSRuntime.
// Handles sign-in popup, token retrieval, and sign-out.
// =============================================================================

import { initializeApp } from "https://www.gstatic.com/firebasejs/11.0.0/firebase-app.js";
import { getAuth, signInWithPopup, GoogleAuthProvider, GithubAuthProvider, signOut, onAuthStateChanged }
    from "https://www.gstatic.com/firebasejs/11.0.0/firebase-auth.js";

let _app = null;
let _auth = null;

window.firebaseAuth = {
    /** Initialize Firebase — call once from Blazor OnInitialized */
    init: function (config) {
        if (_app) return;
        _app = initializeApp(config);
        _auth = getAuth(_app);
    },

    /** Sign in with Google popup. Returns the ID token string. */
    signInWithGoogle: async function () {
        const provider = new GoogleAuthProvider();
        const result = await signInWithPopup(_auth, provider);
        return await result.user.getIdToken();
    },

    /** Sign in with GitHub popup. Returns the ID token string. */
    signInWithGitHub: async function () {
        const provider = new GithubAuthProvider();
        const result = await signInWithPopup(_auth, provider);
        return await result.user.getIdToken();
    },

    /** Get the current user's ID token (refreshed if expired). Returns null if not signed in. */
    getIdToken: async function () {
        const user = _auth?.currentUser;
        if (!user) return null;
        return await user.getIdToken(/* forceRefresh */ false);
    },

    /** Get the current user's profile. Returns null if not signed in. */
    getCurrentUser: function () {
        const user = _auth?.currentUser;
        if (!user) return null;
        return {
            uid: user.uid,
            email: user.email,
            displayName: user.displayName,
            photoURL: user.photoURL
        };
    },

    /** Sign out of Firebase. */
    signOut: async function () {
        if (_auth) await signOut(_auth);
    },

    /** Register a callback for auth state changes. DotNetRef calls back to Blazor. */
    onAuthStateChanged: function (dotNetRef) {
        if (!_auth) return;
        onAuthStateChanged(_auth, async (user) => {
            if (user) {
                const token = await user.getIdToken();
                await dotNetRef.invokeMethodAsync("OnFirebaseAuthStateChanged", token, user.email, user.displayName);
            } else {
                await dotNetRef.invokeMethodAsync("OnFirebaseSignedOut");
            }
        });
    }
};
