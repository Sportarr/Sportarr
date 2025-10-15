# Error Pages Testing Guide

Fightarr now has comprehensive error handling using the `404.png` and `error.png` images. Here's how to test each error page.

---

## 📋 Overview

Three types of error pages have been implemented:

1. **404 Not Found Page** - For invalid URLs
2. **Error Boundary** - For React component errors
3. **Error Page** - For React Router errors (currently not directly used, but available)

---

## 🧪 Test 1: 404 Not Found Page

### What it looks like:
- Displays the `404.png` image
- Shows "404" in large text
- Message: "Page Not Found"
- "Go Back" and "Go to Events" buttons
- Quick links to common pages (Events, Calendar, Activity, Settings)

### How to test:

**Option 1: Invalid URL**
```
http://localhost:7878/this-does-not-exist
http://localhost:7878/invalid-page
http://localhost:7878/xyz123
```

**Option 2: Invalid settings route**
```
http://localhost:7878/settings/invalid
http://localhost:7878/settings/test
```

**Option 3: Misspelled route**
```
http://localhost:7878/evnts  (missing 'e')
http://localhost:7878/calendr (missing 'a')
```

### What to verify:
- ✅ 404.png image displays correctly
- ✅ Error message is clear and professional
- ✅ "Go Back" button works (goes to previous page)
- ✅ "Go to Events" button works (navigates to /events)
- ✅ Quick links work (Events, Calendar, Activity, Settings)
- ✅ Page design matches Fightarr theme (dark, red accents)

---

## 🧪 Test 2: Error Boundary (React Errors)

### What it looks like:
- Displays the `error.png` image
- Shows "Oops!" in large text
- Message: "Application Error"
- Displays the actual error message
- "Reload Page" and "Go to Events" buttons
- Expandable technical details (error stack trace)

### How to test:

**Method 1: Add a test error to a component**

1. Open `frontend/src/pages/EventsPage.tsx`
2. Add this line at the beginning of the component function:
   ```typescript
   throw new Error('Test error for error boundary');
   ```
3. Save the file
4. Rebuild: `npm run build` (in frontend directory)
5. Copy to wwwroot: `powershell -Command "Copy-Item -Path 'f:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr\_output\UI\*' -Destination 'f:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr\src\wwwroot\' -Recurse -Force"`
6. Navigate to http://localhost:7878/events
7. Should see the error boundary page

**Method 2: Browser console**

1. Open browser DevTools (F12)
2. In Console tab, type:
   ```javascript
   throw new Error('Manual test error');
   ```
3. Press Enter
4. This might not trigger the boundary (depends on when it executes)

**Method 3: Add error to a settings page**

1. Open any settings file (e.g., `frontend/src/pages/settings/TagsSettings.tsx`)
2. Add at the start of the component:
   ```typescript
   if (Math.random() > 0) throw new Error('Test error in Tags Settings');
   ```
3. Rebuild and navigate to Settings → Tags

### What to verify:
- ✅ error.png image displays correctly
- ✅ Error message shows the actual error text
- ✅ "Reload Page" button works (refreshes the page)
- ✅ "Go to Events" button works (navigates to /events)
- ✅ Technical details are collapsed by default
- ✅ Can expand technical details to see stack trace
- ✅ Stack trace shows component hierarchy
- ✅ Help section provides useful troubleshooting tips

**Don't forget to remove the test error after testing!**

---

## 🧪 Test 3: Network/API Errors

### What happens:
These don't show the error page, but they demonstrate proper error handling.

### How to test:

**Method 1: Stop the backend**
1. Stop the Fightarr backend server
2. Try to save settings or create a tag
3. Should see "Failed to save" alert messages
4. This is expected behavior (not an error page)

**Method 2: Invalid API response**
1. This would require modifying backend code
2. Not necessary for basic testing

---

## 📸 Visual Verification Checklist

For each error page, verify:

### Design & Branding
- [ ] Matches Fightarr dark theme (gray-900/black gradients)
- [ ] Red accent colors match brand (#ef4444, #dc2626)
- [ ] Consistent button styling with rest of app
- [ ] Professional, not jokey or unprofessional

### Images
- [ ] 404.png displays on 404 page
- [ ] error.png displays on error boundary
- [ ] Images are centered and appropriately sized
- [ ] Images look good on different screen sizes

### Typography
- [ ] Large heading is readable
- [ ] Error messages are clear
- [ ] Help text is not too small
- [ ] Technical details use monospace font

### Buttons
- [ ] All buttons are clickable
- [ ] Hover effects work
- [ ] Button labels are clear
- [ ] Icon + text alignment is correct

### Responsive Design
- [ ] Page looks good on desktop (1920x1080)
- [ ] Page looks good on tablet (768px width)
- [ ] Page looks good on mobile (375px width)
- [ ] Buttons stack properly on small screens

---

## 🔧 Common Issues & Solutions

### Issue: 404 page not showing
**Solution:** Make sure you've rebuilt the frontend and copied to wwwroot.

### Issue: Error boundary not catching errors
**Solution:** Error boundaries only catch errors in child components during rendering. They don't catch:
- Event handler errors (use try/catch)
- Async errors (use try/catch)
- Errors in the error boundary itself

### Issue: Images not loading
**Solution:**
1. Check that 404.png and error.png exist in `frontend/public/`
2. Check that they were copied to `src/wwwroot/`
3. Check browser DevTools Network tab for 404 errors on image requests

### Issue: Styles look wrong
**Solution:** Clear browser cache and reload (Ctrl+Shift+R or Cmd+Shift+R)

---

## 🎯 Quick Test Commands

```bash
# Rebuild frontend
cd "f:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr\frontend"
npm run build

# Copy to wwwroot
cd "f:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr"
powershell -Command "Copy-Item -Path '_output\UI\*' -Destination 'src\wwwroot\' -Recurse -Force"

# Run backend
cd "f:\Downloads\PROGRAM DOWNLOADS\Fightarr\Fightarr\src"
dotnet run
```

Then test these URLs:
- 404 Page: http://localhost:7878/test-404
- Valid Page: http://localhost:7878/events

---

## ✅ Success Criteria

All tests pass when:
1. 404 page shows for invalid URLs
2. Error boundary catches component errors
3. All buttons work correctly
4. Images display properly
5. Design matches Fightarr theme
6. Page is responsive on all screen sizes
7. Technical details are available but not overwhelming
8. Help text is useful and actionable

---

## 📝 Notes

- Error pages are **production-ready** and safe to deploy
- No changes needed to backend code
- All error handling is client-side (React)
- Error details only show in expanded sections (not cluttering the UI)
- Users have clear recovery paths (reload, go home, go back)
