import { initializeApp } from "firebase/app";
import { getAuth, GoogleAuthProvider, inMemoryPersistence, initializeAuth, PhoneAuthProvider, RecaptchaVerifier, signInWithCredential, signInWithPopup, signOut as firebaseSignOut } from "firebase/auth";

let auth;
let phoneAuth;

async function getConfiguredAuth() {
  if (auth) return auth;
  const response = await fetch("/api/auth/config", { credentials: "same-origin", headers: { Accept: "application/json" } });
  if (!response.ok) throw new Error("Firebase configuration is unavailable.");
  const config = await response.json();
  if (!config.configured) throw new Error("Firebase Authentication is not configured.");
  auth = getAuth(initializeApp({
    apiKey: config.apiKey,
    authDomain: config.authDomain,
    projectId: config.projectId,
    appId: config.appId
  }));
  return auth;
}

export async function signInWithGoogle() {
  const provider = new GoogleAuthProvider();
  provider.setCustomParameters({ prompt: "select_account" });
  const result = await signInWithPopup(await getConfiguredAuth(), provider);
  return result.user.getIdToken();
}

export async function signOut() {
  if (auth) await firebaseSignOut(auth);
}

async function profilePhoneUser(expectedUid) {
  const firebase = await getConfiguredAuth();
  await firebase.authStateReady();
  const user = firebase.currentUser;
  if (!user || (expectedUid && user.uid !== expectedUid)) {
    throw new Error("Sign in with Google again to verify your number.");
  }
  return user;
}

export async function profilePhoneToken() {
  return (await profilePhoneUser()).getIdToken();
}

export async function sendProfilePhoneCode(uid, phoneNumber, captcha) {
  await profilePhoneUser(uid);
  // Keep the phone-only Firebase identity separate from the Google sign-in session.
  phoneAuth ??= initializeAuth(initializeApp(auth.app.options, "profile-phone"), { persistence: inMemoryPersistence });
  const verifier = new RecaptchaVerifier(phoneAuth, captcha, { size: "invisible" });
  try {
    return await new PhoneAuthProvider(phoneAuth).verifyPhoneNumber(phoneNumber, verifier);
  } finally {
    verifier.clear();
  }
}

export async function confirmProfilePhoneCode(uid, verificationId, code) {
  await profilePhoneUser(uid);
  const credential = PhoneAuthProvider.credential(verificationId, code);
  const result = await signInWithCredential(phoneAuth, credential);
  try {
    return await result.user.getIdToken();
  } finally {
    await firebaseSignOut(phoneAuth);
  }
}
