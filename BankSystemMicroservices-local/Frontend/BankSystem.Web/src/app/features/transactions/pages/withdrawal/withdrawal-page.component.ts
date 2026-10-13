import { Component } from '@angular/core';

import { TransactionFormComponent } from '../../components/transaction-form.component';

@Component({
  selector: 'app-withdrawal-page',
  standalone: true,
  imports: [TransactionFormComponent],
  template: '<app-transaction-form operation="withdrawal" title="Effectuer un retrait" />',
})
export class WithdrawalPageComponent {}
