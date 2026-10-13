import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { AccountType, CreateAccountRequest } from '../../models/account.models';
import { AccountService } from '../../services/account.service';

@Component({
  selector: 'app-create-account-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './create-account-page.component.html',
  styleUrl: './create-account-page.component.scss',
})
export class CreateAccountPageComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly accountService = inject(AccountService);
  private readonly router = inject(Router);

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = this.formBuilder.nonNullable.group({
    accountType: ['Checking' as AccountType, Validators.required],
    currency: ['USD', [Validators.required, Validators.pattern(/^[A-Z]{3}$/)]],
  });

  submit(): void {
    debugger;
          console.log(this.form)

    if (this.form.invalid) {
      this.form.markAllAsTouched(); return;

     }
    this.error.set(null);
    this.isSubmitting.set(true);
    const request: CreateAccountRequest = this.form.getRawValue();
    debugger;

    this.accountService.create(request).pipe(finalize(() => this.isSubmitting.set(false))).subscribe({
      next: (account) => void this.router.navigate(['/accounts', account.id]),
      error: (error) => this.error.set(error.error?.detail ?? 'Impossible de créer le compte.'),
    });
  }
}
