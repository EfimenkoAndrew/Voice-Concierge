const ADMIN_KEY_STORAGE_KEY = "vc_admin_key";

let _adminKey = localStorage.getItem(ADMIN_KEY_STORAGE_KEY) ?? "";

export const getAdminKey = () => _adminKey;

export const setAdminKey = (k: string) => {
  _adminKey = k;
  if (k) localStorage.setItem(ADMIN_KEY_STORAGE_KEY, k);
  else localStorage.removeItem(ADMIN_KEY_STORAGE_KEY);
};
