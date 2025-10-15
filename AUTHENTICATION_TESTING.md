# Authentication Testing Guide

Complete guide to testing the new Sonarr-style username/password authentication system.

---

## 🎯 Overview

Fightarr now has full username/password authentication with:
- **Two authentication methods**: Forms (Login Page) and Basic (Browser Popup)
- Login page with username/password
- HTTP Basic authentication support
- Session management (cookies)
- Configurable authentication requirements
- "Remember me" functionality
- Local network exemption support
- API key support (alongside sessions)

---

## 🧪 Test Plan

### **Test 1: Enable Authentication**

1. Start Fightarr and navigate to http://localhost:1867
2. Go to **Settings → General → Security**
3. Configure the following:
   - **Authentication Method**: Select "Forms (Login Page)"
   - **Authentication Required**: Select "Enabled"
   - **Username**: `admin`
   - **Password**: `password123`
4. Click **Save Changes**
5. **Refresh the browser or open a new tab**

**Expected Result:**
- You should be redirected to `/login`
- Login page should appear with Fightarr branding
- Should show username and password fields

### **Test 2: Login with Correct Credentials**

1. On the login page, enter:
   - Username: `admin`
   - Password: `password123`
2. Click **Sign In**

**Expected Result:**
- Should redirect to `/events`
- Should be able to access all pages normally
- Should stay logged in when navigating between pages

### **Test 3: Login with Incorrect Credentials**

1. Logout (or open incognito browser)
2. Navigate to http://localhost:1867
3. Should be redirected to login page
4. Enter:
   - Username: `admin`
   - Password: `wrongpassword`
5. Click **Sign In**

**Expected Result:**
- Should show error message: "Invalid username or password"
- Should NOT be logged in
- Should stay on login page
- Should take ~1 second to respond (anti-brute force delay)

### **Test 4: Remember Me Functionality**

1. Login with correct credentials
2. **Check the "Remember me for 30 days" checkbox**
3. Click **Sign In**
4. Once logged in, check browser cookies (F12 → Application → Cookies)

**Expected Result:**
- Cookie named `FightarrSession` should exist
- Cookie should have expiration set to ~30 days from now
- HttpOnly flag should be `true`

**Without Remember Me:**
- Cookie should expire in ~24 hours

### **Test 5: Session Persistence**

1. Login successfully
2. Close the browser completely
3. Reopen browser and navigate to http://localhost:1867

**Expected Result:**
- Should NOT require login again (if within 24 hours or 30 days)
- Should go straight to `/events`

### **Test 6: Logout Functionality**

1. Login successfully
2. Navigate to the app
3. Click user icon or logout button (if added to UI)
4. OR manually call: `fetch('/api/logout', { method: 'POST' })`
5. Try to access any page

**Expected Result:**
- Session should be invalidated
- Cookie should be deleted
- Should be redirected to `/login` when trying to access pages

### **Test 7: Authentication Disabled**

1. Login to Fightarr
2. Go to **Settings → General → Security**
3. Set **Authentication Method** to "None"
4. Click **Save Changes**
5. Logout or open new incognito window
6. Navigate to http://localhost:1867

**Expected Result:**
- Should NOT be redirected to login
- Should go directly to `/events`
- No authentication required

### **Test 8: Local Network Exemption**

1. Login to Fightarr
2. Go to **Settings → General → Security**
3. Set **Authentication Required** to "Disabled for Local Addresses"
4. Click **Save Changes**
5. From local network (192.168.x.x), navigate to Fightarr

**Expected Result:**
- Should NOT require login from local network
- SHOULD require login from external IP addresses

**Testing from External:**
- Use ngrok or port forward to test from external IP
- Should be redirected to login page

### **Test 9: Basic Authentication (Browser Popup)**

1. Login to Fightarr
2. Go to **Settings → General → Security**
3. Set **Authentication Method** to "Basic (Browser Popup)"
4. Set **Authentication Required** to "Enabled"
5. Configure username: `admin` and password: `password123`
6. Click **Save Changes**
7. Logout or open new incognito window
8. Navigate to http://localhost:1867

**Expected Result:**
- Browser should show native authentication popup
- Popup should say "The site says: Fightarr"
- Enter username and password in popup
- Should be granted access after correct credentials
- Should be rejected if incorrect credentials (popup shows again)

**Testing with curl:**
```bash
# Without credentials - should fail with 401 and WWW-Authenticate header
curl -i http://localhost:1867/api/settings

# With Basic auth - should succeed
curl -u admin:password123 http://localhost:1867/api/settings
```

**Expected Response Headers:**
- Without auth: `WWW-Authenticate: Basic realm="Fightarr"`
- With auth: `200 OK` with data

### **Test 10: API Key Still Works**

1. Enable authentication (Test 1 or Test 9)
2. Get API key from: http://localhost:1867/initialize.json
3. Test API endpoint without auth:
   ```bash
   curl http://localhost:1867/api/settings
   ```
   **Should FAIL with 401**

4. Test with API key:
   ```bash
   curl -H "X-Api-Key: YOUR_API_KEY_HERE" http://localhost:1867/api/settings
   ```
   **Should SUCCEED**

**Expected Result:**
- API endpoints work with session cookies, Basic auth, AND API keys
- Allows programmatic access via API key
- Web UI uses session cookies (Forms) or Basic auth
- All three methods work regardless of authentication method setting

### **Test 11: Session Expiration**

This test requires waiting or manually manipulating the database.

**Option A: Wait 24 hours**
1. Login without "remember me"
2. Wait 24+ hours
3. Try to access the app

**Option B: Manually expire session**
1. Login successfully (using Forms authentication)
2. Open database: `data/fightarr.db`
3. Run SQL: `UPDATE AuthSessions SET ExpiresAt = '2020-01-01' WHERE SessionId = 'your-session-id';`
4. Try to access any page

**Expected Result:**
- Should be redirected to login (Forms) or show popup (Basic)
- Expired session should be cleaned up

**Note:** Session expiration only applies to Forms authentication. Basic auth validates credentials on every request.

---

## 🔍 Verification Checklist

After enabling authentication, verify:

### **UI/UX**
- [ ] Login page matches Fightarr branding (dark theme, red accents)
- [ ] Login form is centered and responsive
- [ ] Error messages are clear and helpful
- [ ] Loading state shows when logging in
- [ ] "Remember me" checkbox works
- [ ] Form fields have proper autocomplete attributes

### **Security**
- [ ] Session cookie is HttpOnly (check DevTools)
- [ ] Session cookie is not accessible via JavaScript
- [ ] Failed logins have ~1 second delay
- [ ] Sessions expire after configured time
- [ ] Logout actually invalidates the session
- [ ] API key authentication still works

### **Functionality**
- [ ] Can't access pages without authentication (when enabled)
- [ ] Can access pages after successful login
- [ ] Session persists across browser restarts (if remember me)
- [ ] Session is cleared after logout
- [ ] Local network exemption works (if enabled)
- [ ] Authentication can be disabled entirely

### **Database**
- [ ] AuthSessions table exists
- [ ] Sessions are created on login
- [ ] Sessions are deleted on logout
- [ ] Session includes IP address and user agent
- [ ] Username is stored in settings (SecuritySettings JSON)

---

## 🐛 Troubleshooting

### **Issue: Infinite redirect loop**
**Cause:** Session validation failing even after login
**Solution:**
1. Clear all cookies
2. Check that `FightarrSession` cookie is being set
3. Check browser console for errors

### **Issue: Always redirected to login even with correct credentials**
**Cause:** Cookie not being saved
**Solution:**
1. Check browser privacy settings (allow cookies)
2. Try in incognito mode
3. Check that cookie domain matches your access URL

### **Issue: Can't login - always shows error**
**Cause:** Username/password not configured correctly
**Solution:**
1. Check Settings → General → Security
2. Ensure username and password are saved
3. Check database: `SELECT SecuritySettings FROM AppSettings;`
4. Should contain JSON with username and password

### **Issue: Authentication doesn't work after upgrade**
**Cause:** Database migration not applied
**Solution:**
1. Stop Fightarr
2. Run: `dotnet ef database update` (in src folder)
3. Or delete `data/fightarr.db` to recreate (loses data!)

### **Issue: API calls fail with 401**
**Cause:** API key or session not provided
**Solution:**
- For browser: Ensure logged in (session cookie present)
- For API: Include `X-Api-Key` header with API key

---

## 📝 Quick Test Scripts

### **Forms Authentication (Session Cookies)**

```bash
# 1. Check if auth is required
curl http://localhost:1867/api/auth/check

# 2. Login (replace username/password)
curl -X POST http://localhost:1867/api/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password123","rememberMe":false}' \
  -c cookies.txt

# 3. Test authenticated request
curl http://localhost:1867/api/settings -b cookies.txt

# 4. Logout
curl -X POST http://localhost:1867/api/logout -b cookies.txt
```

### **Basic Authentication (No Sessions)**

```bash
# 1. Test without credentials (should fail with 401)
curl -i http://localhost:1867/api/settings

# 2. Test with Basic auth (should succeed)
curl -u admin:password123 http://localhost:1867/api/settings

# 3. Test with Base64-encoded credentials (equivalent)
# Base64 encode "admin:password123" = "YWRtaW46cGFzc3dvcmQxMjM="
curl -H "Authorization: Basic YWRtaW46cGFzc3dvcmQxMjM=" http://localhost:1867/api/settings
```

### **API Key Authentication (Always Works)**

```bash
# Get API key first
curl http://localhost:1867/initialize.json | grep apiKey

# Use API key
curl -H "X-Api-Key: YOUR_API_KEY_HERE" http://localhost:1867/api/settings
```

---

## ✅ Success Criteria

Authentication is working correctly when:

1. **Forms Auth**: Login page appears and accepts credentials
2. **Basic Auth**: Browser popup appears and accepts credentials
3. **Correct Credentials** allow access to the app
4. **Incorrect Credentials** show error and prevent access
5. **Remember Me** extends session to 30 days (Forms only)
6. **Sessions Persist** across browser restarts (Forms only)
7. **Logout** clears session and requires re-login (Forms only)
8. **Disabled Auth** allows unrestricted access
9. **Local Exemption** works for local network
10. **API Keys** work with any authentication method
11. **Sessions Expire** after configured time (Forms only)
12. **Basic Auth** validates credentials on every request

---

## 🎓 How It Works (Technical)

### **Forms Authentication Flow:**

```
1. User visits any page
2. AuthenticationMiddleware checks if auth required
3. If required, checks for session cookie
4. If no cookie, redirects to /login
5. User enters credentials
6. POST /api/login validates against database settings
7. Creates AuthSession record in database
8. Sets FightarrSession cookie (HttpOnly)
9. Redirects to /events
10. Future requests use cookie for authentication
```

### **Basic Authentication Flow:**

```
1. User visits any page
2. AuthenticationMiddleware checks if auth required
3. If required, checks for Authorization header
4. If no header, sends WWW-Authenticate challenge
5. Browser shows native authentication popup
6. User enters credentials
7. Browser sends Base64-encoded credentials in Authorization header
8. Middleware validates credentials against database settings
9. If valid, allows request
10. Credentials are sent with every subsequent request
```

### **Session Validation (Forms Only):**

```
1. Request arrives with FightarrSession cookie
2. Middleware queries AuthSessions table
3. Checks if session exists and not expired
4. If valid, allows request
5. If invalid/expired, redirects to /login
```

### **Credential Validation (Basic Only):**

```
1. Request arrives with Authorization: Basic header
2. Middleware decodes Base64 credentials
3. Validates username/password against database settings
4. If valid, allows request
5. If invalid, sends 401 with WWW-Authenticate header
6. No session created - credentials validated every request
```

### **Storage:**

- **Username/Password**: Stored in `AppSettings.SecuritySettings` (JSON)
- **Sessions**: Stored in `AuthSessions` table
- **Cookie**: Stored in browser as `FightarrSession`

---

## 🔒 Security Notes

- Passwords are stored in **plain text** in the database (same as Sonarr)
- This is acceptable for self-hosted apps where database access = full control
- For better security, consider using environment variables for credentials
- HTTPS is recommended for production use
- Sessions use HttpOnly cookies (no JavaScript access)
- Failed logins have anti-brute force delay (1 second)
- IP address and User-Agent are logged for security auditing

---

## 📊 Default Settings

- **Authentication Method**: `none` (disabled by default)
- **Authentication Required**: `disabled`
- **Username**: `` (empty - must be configured)
- **Password**: `` (empty - must be configured)
- **Session Duration**: 24 hours (or 30 days with remember me)
- **Cookie Name**: `FightarrSession`
- **Cookie Settings**: HttpOnly, SameSite=Lax

---

Need help? Check the logs or create an issue on GitHub!
