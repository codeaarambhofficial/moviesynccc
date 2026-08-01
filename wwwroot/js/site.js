// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

import { 
    getAuth, 
    signInWithEmailAndPassword, 
    createUserWithEmailAndPassword, 
    signInAnonymously, 
    GoogleAuthProvider, 
    signInWithPopup 
} from "https://www.gstatic.com/firebasejs/10.7.1/firebase-auth.js";

// Initialize Auth (assumes Firebase app is already initialized in your layout)
const auth = getAuth();

// 1. Email/Password Login Function
window.loginWithEmail = async function(email, password) {
    try {
        const userCredential = await signInWithEmailAndPassword(auth, email, password);
        console.log("Logged in:", userCredential.user);
        window.location.href = '/Room';
    } catch (error) {
        console.error("Login error:", error.message);
        alert(error.message);
    }
}

// 2. Anonymous Login Function
window.loginAnonymously = async function() {
    try {
        const result = await signInAnonymously(auth);
        console.log("Logged in anonymously:", result.user.uid);
        window.location.href = '/Room';
    } catch (error) {
        console.error("Anonymous auth error:", error.message);
        alert(error.message);
    }
}

// 3. Google Sign-In Function
window.loginWithGoogle = async function() {
    try {
        const provider = new GoogleAuthProvider();
        const result = await signInWithPopup(auth, provider);
        console.log("Google user signed in:", result.user.displayName);
        window.location.href = '/Room';
    } catch (error) {
        console.error("Google auth error:", error.message);
        alert(error.message);
    }
}