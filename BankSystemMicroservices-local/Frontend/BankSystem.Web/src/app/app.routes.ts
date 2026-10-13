import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { AccountDetailsPageComponent } from './features/accounts/pages/account-details/account-details-page.component';
import { AccountsPageComponent } from './features/accounts/pages/accounts/accounts-page.component';
import { CreateAccountPageComponent } from './features/accounts/pages/create-account/create-account-page.component';
import { LoginPageComponent } from './features/authentication/pages/login/login-page.component';
import { RegisterPageComponent } from './features/authentication/pages/register/register-page.component';
import { DashboardPageComponent } from './features/dashboard/pages/dashboard-page.component';
import { MovementsPageComponent } from './features/movements/pages/movements-page.component';
import { NotificationsPageComponent } from './features/notifications/pages/notifications-page.component';
import { ReportsPageComponent } from './features/reports/pages/reports-page.component';
import { DepositPageComponent } from './features/transactions/pages/deposit/deposit-page.component';
import { TransferPageComponent } from './features/transactions/pages/transfer/transfer-page.component';
import { PaymentHistoryPageComponent } from './features/transactions/pages/payment-history/payment-history-page.component';
import { WithdrawalPageComponent } from './features/transactions/pages/withdrawal/withdrawal-page.component';
import { AppShellComponent } from './layout/app-shell.component';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginPageComponent,
    title: 'Connexion | Bank System',
  },
  {
    path: 'register',
    component: RegisterPageComponent,
    title: 'Inscription | Bank System',
  },
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard',
  },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'accounts',
        component: AccountsPageComponent,
        title: 'Mes comptes | Bank System',
      },
      {
        path: 'accounts/new',
        component: CreateAccountPageComponent,
        title: 'Ouvrir un compte | Bank System',
      },
      {
        path: 'accounts/:id',
        component: AccountDetailsPageComponent,
        title: 'Détail du compte | Bank System',
      },
      {
        path: 'transactions/deposit',
        component: DepositPageComponent,
        title: 'Dépôt | Bank System',
      },
      {
        path: 'transactions/withdrawal',
        component: WithdrawalPageComponent,
        title: 'Retrait | Bank System',
      },
      {
        path: 'transactions/transfer',
        component: TransferPageComponent,
        title: 'Virement | Bank System',
      },
      {
        path: 'payments',
        component: PaymentHistoryPageComponent,
        title: 'Paiements | Bank System',
      },
      {
        path: 'movements',
        component: MovementsPageComponent,
        title: 'Mouvements | Bank System',
      },
      {
        path: 'notifications',
        component: NotificationsPageComponent,
        title: 'Notifications | Bank System',
      },
      {
        path: 'reports',
        component: ReportsPageComponent,
        title: 'Rapports | Bank System',
      },
      {
        path: 'dashboard',
        component: DashboardPageComponent,
        title: 'Tableau de bord | Bank System',
      },
    ],
  },
  {
    path: '**',
    redirectTo: 'dashboard',
  },
];
