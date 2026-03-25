// JavaScript source code
// Import the functions you need from the SDKs you need
import { initializeApp } from "firebase/app";
import { getAnalytics } from "firebase/analytics";
// TODO: Add SDKs for Firebase products that you want to use
// https://firebase.google.com/docs/web/setup#available-libraries

// Your web app's Firebase configuration
// For Firebase JS SDK v7.20.0 and later, measurementId is optional
const firebaseConfig = {
  apiKey: "AIzaSyAzPNS2_TSz72DO4yo1UpW3JSVQO_f6kzk",
  authDomain: "gen-lang-client-0590872284.firebaseapp.com",
  projectId: "gen-lang-client-0590872284",
  storageBucket: "gen-lang-client-0590872284.firebasestorage.app",
  messagingSenderId: "125253446377",
  appId: "1:125253446377:web:0ada2fe9601aaaf5ab1e78",
  measurementId: "G-4XBNDBRXCS"
};

// Initialize Firebase
const app = initializeApp(firebaseConfig);
const analytics = getAnalytics(app);