import { initializeApp } from "firebase/app";
import { getAuth, GoogleAuthProvider, signInWithPopup, signOut as firebaseSignOut } from "firebase/auth";

let auth;

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
