# Purpose
This application checks the [Flying Pie website](http://www.flyingpie.com/its-your-day.htm) for their latest "**It's Your Day**" names, and sends email notifications when any changes are made to the list.

# Setup
I recommend creating a new Google account for this so that if anything goes horribly wrong, your real account will be unharmed. Then set up an [app-specific password](https://security.google.com/settings/security/apppasswords) for this application so it can send emails.

Next, copy the file FlyingPie\_example.config as FlyingPie.config and replace the sample values with your own.

 - FromEmailAddress is the email address of the account you made for this.
 - ErrorEmails is a comma-separated list of email addresses which should be notified if something goes wrong.
 - UpdateEmails is a comma-separated list of email addresses which will be notified when new IYD names are posted.
 - SaltyString should be a long string of random characters. It's used to help encrypt the password for your email account.

Now you should be ready to build and run the application. The first time you start, it will ask for the app-specific password you set up earlier. This password will be encrypted using Windows' DPAPI so it should only be able to be decrypted by the currently-logged-in user: you should have a strong password on your Windows account! The encrypted password is saved in the app.config so that when this application runs in the future, it won't have to prompt you for the password. Once that's done, the app should silently continue on to run a check for new IYD events, then exit. Future calls to this application from the same user should be quick and painless, requiring no user input.
